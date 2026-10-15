-- 002: row-level security as a backstop for tenant isolation.
-- The API sets SESSION_CONTEXT(N'TenantId') (read-only) on every connection it opens. Every stored
-- procedure also filters by @TenantId; if one ever forgets, these predicates still hide and block
-- other tenants' rows. 'Maintenance' is only set by the migration runner and the dev token endpoint.
CREATE FUNCTION rls.fn_TenantAccess (@TenantId UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS AccessGranted
    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER)
       OR CAST(SESSION_CONTEXT(N'Maintenance') AS BIT) = 1;
GO

CREATE SECURITY POLICY rls.TenantIsolation
    ADD FILTER PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.Users,
    ADD BLOCK PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.Users AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.Users AFTER UPDATE,
    ADD FILTER PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.LabEvents,
    ADD BLOCK PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.LabEvents AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.LabEvents AFTER UPDATE,
    ADD FILTER PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.Bookings,
    ADD BLOCK PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.Bookings AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_TenantAccess (TenantId) ON dbo.Bookings AFTER UPDATE
WITH (STATE = ON);
