-- Per-user Google Gemini API key for Tracker AI "Summarize" (each user uses their OWN free key so
-- there's no shared rate limit). Read/written by UserAdminRepository (SELECT/UPDATE app.Users.GeminiApiKey)
-- and surfaced in Settings → AI Summary Key. Idempotent — safe to re-run.
--
-- NOTE (2026-09-07): this column already exists on the SERVER (52.66.183.53 / IndusOrbit) — it was
-- added directly there in an earlier session with no committed script. This file backfills that gap so
-- a fresh IndusTaskManagement / IndusOrbit setup includes it.
IF COL_LENGTH('app.Users', 'GeminiApiKey') IS NULL
    ALTER TABLE app.Users ADD GeminiApiKey nvarchar(200) NULL;
