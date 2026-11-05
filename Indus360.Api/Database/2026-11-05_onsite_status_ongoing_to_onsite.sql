-- Onsite Management: the status "Ongoing" is now called "Onsite".
-- Run once on each database (local is done on 2026-11-05; prod IndusOrbit: run at deploy time).

SELECT COUNT(*) AS RowsToChange FROM app.OnsiteVisits WHERE Status = 'Ongoing';

UPDATE app.OnsiteVisits SET Status = 'Onsite' WHERE Status = 'Ongoing';

-- Must return 0.
SELECT COUNT(*) AS StillOngoing FROM app.OnsiteVisits WHERE Status = 'Ongoing';
