-- 004: demo organizations, people and events for local development and the test suite.
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants)
BEGIN
    DECLARE @Contoso UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
    DECLARE @Fabrikam UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
    DECLARE @Rahul UNIQUEIDENTIFIER = 'a0000000-0000-0000-0000-000000000001';
    DECLARE @Lena UNIQUEIDENTIFIER = 'b0000000-0000-0000-0000-000000000001';
    DECLARE @Today DATETIME2(0) = CAST(CAST(SYSUTCDATETIME() AS DATE) AS DATETIME2(0));

    INSERT dbo.Tenants (TenantId, Name) VALUES
        (@Contoso, N'Contoso Academy'),
        (@Fabrikam, N'Fabrikam University');

    INSERT dbo.Users (UserId, TenantId, Email, DisplayName, Role) VALUES
        (@Rahul, @Contoso, N'rahul@contoso.example', N'Rahul Menon', 'instructor'),
        ('a0000000-0000-0000-0000-000000000002', @Contoso, N'ananya@contoso.example', N'Ananya Sen', 'learner'),
        ('a0000000-0000-0000-0000-000000000003', @Contoso, N'kabir@contoso.example', N'Kabir Shah', 'learner'),
        ('a0000000-0000-0000-0000-000000000004', @Contoso, N'zoya@contoso.example', N'Zoya Khan', 'learner'),
        (@Lena, @Fabrikam, N'lena@fabrikam.example', N'Lena Fischer', 'instructor'),
        ('b0000000-0000-0000-0000-000000000002', @Fabrikam, N'arjun@fabrikam.example', N'Arjun Das', 'learner');

    INSERT dbo.LabEvents (EventId, TenantId, Title, LabTemplate, StartsAt, EndsAt, Capacity, CreatedBy) VALUES
        (NEWID(), @Contoso, N'AKS bootcamp: deploy, scale, observe', N'aks-fundamentals',
            DATEADD(HOUR, 33, @Today), DATEADD(HOUR, 36, @Today), 12, @Rahul),
        (NEWID(), @Contoso, N'Secure secrets with Key Vault', N'keyvault-rotation',
            DATEADD(HOUR, 57, @Today), DATEADD(HOUR, 59, @Today), 2, @Rahul),
        (NEWID(), @Contoso, N'RAG chatbot with Azure OpenAI', N'aoai-rag',
            DATEADD(HOUR, 105, @Today), DATEADD(HOUR, 108, @Today), 20, @Rahul),
        (NEWID(), @Fabrikam, N'Linux VM fundamentals', N'linux-vm',
            DATEADD(HOUR, 34, @Today), DATEADD(HOUR, 36, @Today), 15, @Lena);
END
