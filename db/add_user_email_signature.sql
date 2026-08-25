-- Per-user HTML email signature (auto-appended when composing in Indus 360).
SET NOCOUNT ON;
IF COL_LENGTH('app.Users', 'EmailSignature') IS NULL
BEGIN
    ALTER TABLE app.Users ADD EmailSignature nvarchar(max) NULL;
    SELECT 'EmailSignature column added' AS Result;
END
ELSE SELECT 'EmailSignature already exists' AS Result;
