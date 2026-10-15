-- 001: core tables. Every tenant-owned table carries TenantId; row-level security is added in 002.
IF SCHEMA_ID(N'rls') IS NULL EXEC (N'CREATE SCHEMA rls');
GO

CREATE TABLE dbo.Tenants
(
    TenantId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
    Name        NVARCHAR(120)    NOT NULL,
    CreatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Tenants_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Users
(
    UserId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Users_Tenants REFERENCES dbo.Tenants (TenantId),
    Email       NVARCHAR(254)    NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
    DisplayName NVARCHAR(120)    NOT NULL,
    Role        VARCHAR(20)      NOT NULL CONSTRAINT CK_Users_Role CHECK (Role IN ('learner', 'instructor')),
    CreatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Users_Tenant ON dbo.Users (TenantId, Role);

CREATE TABLE dbo.LabEvents
(
    EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_LabEvents PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_LabEvents_Tenants REFERENCES dbo.Tenants (TenantId),
    Title       NVARCHAR(120)    NOT NULL,
    LabTemplate NVARCHAR(80)     NOT NULL,
    StartsAt    DATETIME2(0)     NOT NULL,
    EndsAt      DATETIME2(0)     NOT NULL,
    Capacity    INT              NOT NULL CONSTRAINT CK_LabEvents_Capacity CHECK (Capacity BETWEEN 1 AND 500),
    CreatedBy   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_LabEvents_Users REFERENCES dbo.Users (UserId),
    CreatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_LabEvents_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_LabEvents_Window CHECK (EndsAt > StartsAt)
);
CREATE INDEX IX_LabEvents_Tenant_Starts ON dbo.LabEvents (TenantId, StartsAt);

CREATE TABLE dbo.Bookings
(
    BookingId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY,
    TenantId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Bookings_Tenants REFERENCES dbo.Tenants (TenantId),
    EventId          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Bookings_LabEvents REFERENCES dbo.LabEvents (EventId),
    LearnerId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Bookings_Users REFERENCES dbo.Users (UserId),
    Status           VARCHAR(12)      NOT NULL CONSTRAINT CK_Bookings_Status CHECK (Status IN ('confirmed', 'waitlisted', 'cancelled')),
    WaitlistPosition INT              NULL,
    IdempotencyKey   NVARCHAR(100)    NULL,
    CreatedAt        DATETIME2(3)     NOT NULL CONSTRAINT DF_Bookings_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt        DATETIME2(3)     NOT NULL CONSTRAINT DF_Bookings_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Bookings_Waitlist CHECK (
        (Status = 'waitlisted' AND WaitlistPosition IS NOT NULL) OR (Status <> 'waitlisted' AND WaitlistPosition IS NULL))
);

-- Business rules the database enforces even if application code gets them wrong.
CREATE UNIQUE INDEX UX_Bookings_OneActivePerLearner ON dbo.Bookings (EventId, LearnerId)
    WHERE Status IN ('confirmed', 'waitlisted');
CREATE UNIQUE INDEX UX_Bookings_Idempotency ON dbo.Bookings (LearnerId, IdempotencyKey)
    WHERE IdempotencyKey IS NOT NULL;
CREATE INDEX IX_Bookings_Event_Status ON dbo.Bookings (EventId, Status, WaitlistPosition) INCLUDE (LearnerId);
CREATE INDEX IX_Bookings_Learner ON dbo.Bookings (LearnerId, Status) INCLUDE (EventId);
