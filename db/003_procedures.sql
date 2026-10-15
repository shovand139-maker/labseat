-- 003: stored procedures. All data access from the API goes through these.
-- Error numbers (mapped to HTTP problem responses by the API):
--   50001 event not found (404)      50005 booking not found (404)
--   50003 booking closed (409)       50006 booking already cancelled (409)
--   50004 capacity conflict (409)    50007 not allowed (403)
--   50008 invalid request (422)

CREATE OR ALTER PROCEDURE dbo.usp_Event_Get
    @TenantId UNIQUEIDENTIFIER,
    @EventId  UNIQUEIDENTIFIER,
    @LearnerId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT e.EventId, e.Title, e.LabTemplate,
           TODATETIMEOFFSET(e.StartsAt, 0) AS StartsAt,
           TODATETIMEOFFSET(e.EndsAt, 0)   AS EndsAt,
           e.Capacity,
           ISNULL(c.Confirmed, 0)  AS Confirmed,
           ISNULL(c.Waitlisted, 0) AS Waitlisted,
           mine.BookingId          AS MyBookingId,
           mine.Status             AS MyStatus
    FROM dbo.LabEvents AS e
    OUTER APPLY (
        SELECT SUM(CASE WHEN b.Status = 'confirmed' THEN 1 ELSE 0 END)  AS Confirmed,
               SUM(CASE WHEN b.Status = 'waitlisted' THEN 1 ELSE 0 END) AS Waitlisted
        FROM dbo.Bookings AS b
        WHERE b.EventId = e.EventId AND b.TenantId = @TenantId
    ) AS c
    OUTER APPLY (
        SELECT TOP (1) b.BookingId, b.Status
        FROM dbo.Bookings AS b
        WHERE b.EventId = e.EventId AND b.TenantId = @TenantId
          AND b.LearnerId = @LearnerId AND b.Status IN ('confirmed', 'waitlisted')
    ) AS mine
    WHERE e.EventId = @EventId AND e.TenantId = @TenantId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Event_List
    @TenantId  UNIQUEIDENTIFIER,
    @From      DATETIME2(0),
    @To        DATETIME2(0),
    @LearnerId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT e.EventId, e.Title, e.LabTemplate,
           TODATETIMEOFFSET(e.StartsAt, 0) AS StartsAt,
           TODATETIMEOFFSET(e.EndsAt, 0)   AS EndsAt,
           e.Capacity,
           ISNULL(c.Confirmed, 0)  AS Confirmed,
           ISNULL(c.Waitlisted, 0) AS Waitlisted,
           mine.BookingId          AS MyBookingId,
           mine.Status             AS MyStatus
    FROM dbo.LabEvents AS e
    OUTER APPLY (
        SELECT SUM(CASE WHEN b.Status = 'confirmed' THEN 1 ELSE 0 END)  AS Confirmed,
               SUM(CASE WHEN b.Status = 'waitlisted' THEN 1 ELSE 0 END) AS Waitlisted
        FROM dbo.Bookings AS b
        WHERE b.EventId = e.EventId AND b.TenantId = @TenantId
    ) AS c
    OUTER APPLY (
        SELECT TOP (1) b.BookingId, b.Status
        FROM dbo.Bookings AS b
        WHERE b.EventId = e.EventId AND b.TenantId = @TenantId
          AND b.LearnerId = @LearnerId AND b.Status IN ('confirmed', 'waitlisted')
    ) AS mine
    WHERE e.TenantId = @TenantId AND e.StartsAt >= @From AND e.StartsAt < @To
    ORDER BY e.StartsAt, e.Title;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Event_Create
    @TenantId    UNIQUEIDENTIFIER,
    @EventId     UNIQUEIDENTIFIER,
    @Title       NVARCHAR(120),
    @LabTemplate NVARCHAR(80),
    @StartsAt    DATETIME2(0),
    @EndsAt      DATETIME2(0),
    @Capacity    INT,
    @CreatedBy   UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @EndsAt <= @StartsAt
        THROW 50008, N'The event must end after it starts.', 1;
    IF @Capacity < 1 OR @Capacity > 500
        THROW 50008, N'Capacity must be between 1 and 500.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE UserId = @CreatedBy AND TenantId = @TenantId AND Role = 'instructor')
        THROW 50007, N'Only instructors in this organization can create events.', 1;

    INSERT dbo.LabEvents (EventId, TenantId, Title, LabTemplate, StartsAt, EndsAt, Capacity, CreatedBy)
    VALUES (@EventId, @TenantId, @Title, @LabTemplate, @StartsAt, @EndsAt, @Capacity, @CreatedBy);

    EXEC dbo.usp_Event_Get @TenantId = @TenantId, @EventId = @EventId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Booking_Get
    @TenantId  UNIQUEIDENTIFIER,
    @BookingId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT b.BookingId, b.EventId, e.Title AS EventTitle,
           TODATETIMEOFFSET(e.StartsAt, 0) AS StartsAt,
           b.LearnerId, b.Status,
           CASE WHEN b.Status = 'waitlisted' THEN (
               SELECT COUNT(*) FROM dbo.Bookings AS w
               WHERE w.EventId = b.EventId AND w.TenantId = @TenantId
                 AND w.Status = 'waitlisted' AND w.WaitlistPosition <= b.WaitlistPosition)
           END AS QueuePosition,
           TODATETIMEOFFSET(b.CreatedAt, 0) AS CreatedAt
    FROM dbo.Bookings AS b
    JOIN dbo.LabEvents AS e ON e.EventId = b.EventId AND e.TenantId = @TenantId
    WHERE b.BookingId = @BookingId AND b.TenantId = @TenantId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Booking_Create
    @TenantId       UNIQUEIDENTIFIER,
    @EventId        UNIQUEIDENTIFIER,
    @LearnerId      UNIQUEIDENTIFIER,
    @IdempotencyKey NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(0) = SYSUTCDATETIME();
    DECLARE @Capacity INT, @StartsAt DATETIME2(0), @Confirmed INT, @NextPosition INT;
    DECLARE @BookingId UNIQUEIDENTIFIER, @ExistingEventId UNIQUEIDENTIFIER;

    BEGIN TRANSACTION;

    -- Serialize all seat changes for one event: every writer takes this update lock first.
    SELECT @Capacity = Capacity, @StartsAt = StartsAt
    FROM dbo.LabEvents WITH (UPDLOCK, HOLDLOCK)
    WHERE EventId = @EventId AND TenantId = @TenantId;

    IF @Capacity IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50001, N'Event not found.', 1;
    END
    IF @StartsAt <= @Now
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50003, N'Booking is closed because the event has started.', 1;
    END
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE UserId = @LearnerId AND TenantId = @TenantId AND Role = 'learner')
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50007, N'Only learners in this organization can book seats.', 1;
    END

    -- A retried request (same Idempotency-Key) or a second click returns the booking that already exists.
    IF @IdempotencyKey IS NOT NULL
        SELECT @BookingId = BookingId, @ExistingEventId = EventId
        FROM dbo.Bookings
        WHERE TenantId = @TenantId AND LearnerId = @LearnerId AND IdempotencyKey = @IdempotencyKey;

    IF @BookingId IS NOT NULL AND @ExistingEventId <> @EventId
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50008, N'This Idempotency-Key was already used for a different event.', 1;
    END

    IF @BookingId IS NULL
        SELECT @BookingId = BookingId
        FROM dbo.Bookings
        WHERE TenantId = @TenantId AND EventId = @EventId AND LearnerId = @LearnerId
          AND Status IN ('confirmed', 'waitlisted');

    IF @BookingId IS NULL
    BEGIN
        SET @BookingId = NEWID();

        SELECT @Confirmed = COUNT(*)
        FROM dbo.Bookings
        WHERE EventId = @EventId AND TenantId = @TenantId AND Status = 'confirmed';

        IF @Confirmed < @Capacity
            INSERT dbo.Bookings (BookingId, TenantId, EventId, LearnerId, Status, WaitlistPosition, IdempotencyKey)
            VALUES (@BookingId, @TenantId, @EventId, @LearnerId, 'confirmed', NULL, @IdempotencyKey);
        ELSE
        BEGIN
            SELECT @NextPosition = ISNULL(MAX(WaitlistPosition), 0) + 1
            FROM dbo.Bookings
            WHERE EventId = @EventId AND TenantId = @TenantId AND Status = 'waitlisted';

            INSERT dbo.Bookings (BookingId, TenantId, EventId, LearnerId, Status, WaitlistPosition, IdempotencyKey)
            VALUES (@BookingId, @TenantId, @EventId, @LearnerId, 'waitlisted', @NextPosition, @IdempotencyKey);
        END
    END

    COMMIT TRANSACTION;

    EXEC dbo.usp_Booking_Get @TenantId = @TenantId, @BookingId = @BookingId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Booking_Cancel
    @TenantId          UNIQUEIDENTIFIER,
    @BookingId         UNIQUEIDENTIFIER,
    @ActorId           UNIQUEIDENTIFIER,
    @ActorIsInstructor BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @EventId UNIQUEIDENTIFIER, @LearnerId UNIQUEIDENTIFIER, @Status VARCHAR(12), @PromotedId UNIQUEIDENTIFIER;

    SELECT @EventId = EventId FROM dbo.Bookings WHERE BookingId = @BookingId AND TenantId = @TenantId;
    IF @EventId IS NULL
        THROW 50005, N'Booking not found.', 1;

    BEGIN TRANSACTION;

    -- Same lock order as usp_Booking_Create (event row first), so the two cannot deadlock.
    SELECT @EventId = EventId
    FROM dbo.LabEvents WITH (UPDLOCK, HOLDLOCK)
    WHERE EventId = @EventId AND TenantId = @TenantId;

    SELECT @LearnerId = LearnerId, @Status = Status
    FROM dbo.Bookings WITH (UPDLOCK)
    WHERE BookingId = @BookingId AND TenantId = @TenantId;

    -- Learners can only cancel their own booking; anyone else's looks like it does not exist.
    IF @ActorIsInstructor = 0 AND @LearnerId <> @ActorId
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50005, N'Booking not found.', 1;
    END
    IF @Status = 'cancelled'
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50006, N'This booking is already cancelled.', 1;
    END

    UPDATE dbo.Bookings
    SET Status = 'cancelled', WaitlistPosition = NULL, UpdatedAt = SYSUTCDATETIME()
    WHERE BookingId = @BookingId AND TenantId = @TenantId;

    -- A freed seat goes to the first person in the waitlist.
    IF @Status = 'confirmed'
    BEGIN
        SELECT TOP (1) @PromotedId = BookingId
        FROM dbo.Bookings WITH (UPDLOCK)
        WHERE EventId = @EventId AND TenantId = @TenantId AND Status = 'waitlisted'
        ORDER BY WaitlistPosition;

        IF @PromotedId IS NOT NULL
            UPDATE dbo.Bookings
            SET Status = 'confirmed', WaitlistPosition = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE BookingId = @PromotedId AND TenantId = @TenantId;
    END

    COMMIT TRANSACTION;

    SELECT @BookingId AS CancelledBookingId, @PromotedId AS PromotedBookingId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Event_SetCapacity
    @TenantId UNIQUEIDENTIFIER,
    @EventId  UNIQUEIDENTIFIER,
    @Capacity INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Confirmed INT, @Exists BIT = 0, @Free INT;

    IF @Capacity < 1 OR @Capacity > 500
        THROW 50008, N'Capacity must be between 1 and 500.', 1;

    BEGIN TRANSACTION;

    SELECT @Exists = 1
    FROM dbo.LabEvents WITH (UPDLOCK, HOLDLOCK)
    WHERE EventId = @EventId AND TenantId = @TenantId;

    IF @Exists = 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50001, N'Event not found.', 1;
    END

    SELECT @Confirmed = COUNT(*)
    FROM dbo.Bookings
    WHERE EventId = @EventId AND TenantId = @TenantId AND Status = 'confirmed';

    IF @Capacity < @Confirmed
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50004, N'Capacity cannot go below the number of confirmed seats. Cancel bookings first.', 1;
    END

    UPDATE dbo.LabEvents SET Capacity = @Capacity WHERE EventId = @EventId AND TenantId = @TenantId;

    -- Extra seats go to the waitlist in order.
    SET @Free = @Capacity - @Confirmed;
    WITH NextInLine AS (
        SELECT TOP (@Free) BookingId
        FROM dbo.Bookings
        WHERE EventId = @EventId AND TenantId = @TenantId AND Status = 'waitlisted'
        ORDER BY WaitlistPosition
    )
    UPDATE b
    SET Status = 'confirmed', WaitlistPosition = NULL, UpdatedAt = SYSUTCDATETIME()
    FROM dbo.Bookings AS b
    JOIN NextInLine AS n ON n.BookingId = b.BookingId;

    COMMIT TRANSACTION;

    EXEC dbo.usp_Event_Get @TenantId = @TenantId, @EventId = @EventId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Event_Roster
    @TenantId UNIQUEIDENTIFIER,
    @EventId  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.LabEvents WHERE EventId = @EventId AND TenantId = @TenantId)
        THROW 50001, N'Event not found.', 1;

    SELECT b.BookingId, b.LearnerId, u.DisplayName, u.Email, b.Status,
           CASE WHEN b.Status = 'waitlisted'
                THEN CAST(ROW_NUMBER() OVER (PARTITION BY b.Status ORDER BY b.WaitlistPosition) AS INT)
           END AS QueuePosition,
           TODATETIMEOFFSET(b.CreatedAt, 0) AS CreatedAt
    FROM dbo.Bookings AS b
    JOIN dbo.Users AS u ON u.UserId = b.LearnerId AND u.TenantId = @TenantId
    WHERE b.EventId = @EventId AND b.TenantId = @TenantId AND b.Status IN ('confirmed', 'waitlisted')
    ORDER BY CASE b.Status WHEN 'confirmed' THEN 0 ELSE 1 END, b.WaitlistPosition, b.CreatedAt;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Booking_ListForLearner
    @TenantId  UNIQUEIDENTIFIER,
    @LearnerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT b.BookingId, b.EventId, e.Title AS EventTitle,
           TODATETIMEOFFSET(e.StartsAt, 0) AS StartsAt,
           b.LearnerId, b.Status,
           CASE WHEN b.Status = 'waitlisted' THEN (
               SELECT COUNT(*) FROM dbo.Bookings AS w
               WHERE w.EventId = b.EventId AND w.TenantId = @TenantId
                 AND w.Status = 'waitlisted' AND w.WaitlistPosition <= b.WaitlistPosition)
           END AS QueuePosition,
           TODATETIMEOFFSET(b.CreatedAt, 0) AS CreatedAt
    FROM dbo.Bookings AS b
    JOIN dbo.LabEvents AS e ON e.EventId = b.EventId AND e.TenantId = @TenantId
    WHERE b.TenantId = @TenantId AND b.LearnerId = @LearnerId
    ORDER BY e.StartsAt DESC;
END
GO
