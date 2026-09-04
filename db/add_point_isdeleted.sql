-- Soft-delete flag for Point Management points (app-wide convention: UI deletes set
-- IsDeletedTransaction=1, never hard-delete; every point-grid read filters it out).
-- Used by Manage Points → Delete on a Queue-status point.
IF COL_LENGTH('dbo.Points', 'IsDeletedTransaction') IS NULL
    ALTER TABLE dbo.Points ADD IsDeletedTransaction bit NOT NULL CONSTRAINT DF_Points_IsDeleted DEFAULT(0);
