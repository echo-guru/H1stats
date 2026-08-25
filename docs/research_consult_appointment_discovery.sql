-- =============================================================================
-- Research Patient Finder — CM2 upcoming CONSULT appointments
-- =============================================================================
-- Authoritative source: GPH_patList_importer SMS module
--   backend/src/services/outlookScheduleService.ts  (getUpcomingSchedule)
--   backend/src/utils/consultServiceTitleMatch.ts   (consult title patterns)
--
-- Key facts (already proven in production SMS):
--   • Bookings: SCHEDULE_APPOINTMENT + SCHEDULE_OCC_SERVICE.OCC_SERVICE_TITLE
--   • Patient ID: PATIENT.PAT_ID
--   • Cardiologist: PATIENT.PAT_CARDIOLOGIST (RID), NOT same-day TEST
--   • Site: SCHEDULE_ROOM → SCHEDULE_SITE.SITE_NAME
--   • Consult filter: OCC_SERVICE_TITLE regex (not occ-service RID list)
--   • Completed consult letters: CONSULT + LETTERS (separate from diary)
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1) Consult-only upcoming appointments (Research Patient Finder list query)
--    One row per booking (do NOT merge patient-day like SMS).
--    Bind :startDate / :endDate as YYYY-MM-DD; end date is inclusive.
-- -----------------------------------------------------------------------------
SELECT
    A.SCHEDULE_APPOINTMENT_RID,
    P.PAT_ID,
    P.PAT_FIRSTNAME,
    P.PAT_LASTNAME,
    A.APPOINTMENT_DATE,
    A.APPOINTMENT_TIME,
    S.OCC_SERVICE_TITLE,
    TRIM(NVL(ST.SITE_NAME, ' ')) AS SITE_NAME,
    P.PAT_DOB,
    P.PAT_CARDIOLOGIST,
    A.APPOINTMENT_NOTE
FROM HEARTS1ST.SCHEDULE_APPOINTMENT A
JOIN HEARTS1ST.PATIENT P
  ON P.PATIENT_RID = A.PATIENT_RID
LEFT JOIN HEARTS1ST.SCHEDULE_OCC_SERVICE S
  ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
LEFT JOIN HEARTS1ST.SCHEDULE_ROOM R
  ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
LEFT JOIN HEARTS1ST.SCHEDULE_SITE ST
  ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
WHERE A.APPOINTMENT_DATE >= TO_DATE(:startDate, 'YYYY-MM-DD')
  AND A.APPOINTMENT_DATE < TO_DATE(:endDate, 'YYYY-MM-DD') + 1
  AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
-- Same consult titles as GPH SMS consultServiceTitleMatch.
-- Oracle REGEXP does not support JS \s — use [[:space:]] or LIKE.
AND (
      REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'INITIAL[[:space:]-]*CONSULT')
   OR REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'FOLLOW[[:space:]-]*UP[[:space:]-]*CONSULT')
   OR REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'FOLLOW[[:space:]-]*UP[[:space:]-]*-[[:space:]-]*NEW[[:space:]-]*REFERRAL')
   OR UPPER(NVL(S.OCC_SERVICE_TITLE, '')) LIKE '%CONSULT%'
   OR UPPER(NVL(S.OCC_SERVICE_TITLE, '')) LIKE '%NEW REFERRAL%'
    )
ORDER BY A.APPOINTMENT_DATE, A.APPOINTMENT_TIME, P.PAT_ID;


-- -----------------------------------------------------------------------------
-- 2) Optional cardiologist filter (PAT_CARDIOLOGIST RID — same as SMS module)
--    Known RIDs (GPH + H1Stats Cm2CardiologistMapping):
--      981  Dr Jeremy Wright
--      2901 Dr Shi Yi Goo
--      1681 Dr Vance Manins
--      144  Dr Roess Pascoe
--      1941 Dr Justin Morze
--      282  Dr Julie Ch'ng
--    Add: AND P.PAT_CARDIOLOGIST = :cardiologistRid
-- -----------------------------------------------------------------------------


-- -----------------------------------------------------------------------------
-- 3) Optional site filter (SMS uses site name, maps Greenslopes→GPH etc.)
--    Add: AND UPPER(TRIM(NVL(ST.SITE_NAME, ' '))) = UPPER(TRIM(:siteName))
-- -----------------------------------------------------------------------------


-- -----------------------------------------------------------------------------
-- 4) All distinct consult service titles (verify patterns cover real diary)
-- -----------------------------------------------------------------------------
SELECT
    TRIM(OCC_SERVICE_TITLE) AS service_title,
    COUNT(*) AS booking_count
FROM HEARTS1ST.SCHEDULE_OCC_SERVICE S
JOIN HEARTS1ST.SCHEDULE_APPOINTMENT A
  ON A.SCHEDULE_OCC_SERVICE_RID = S.SCHEDULE_OCC_SERVICE_RID
WHERE A.APPOINTMENT_DATE >= TRUNC(SYSDATE)
  AND A.APPOINTMENT_DATE < TRUNC(SYSDATE) + 90
GROUP BY TRIM(OCC_SERVICE_TITLE)
HAVING REGEXP_LIKE(NVL(TRIM(OCC_SERVICE_TITLE), ''), 'consult|referral', 'i')
ORDER BY booking_count DESC;


-- -----------------------------------------------------------------------------
-- 5) Full day schedule (all service types — same as SMS getUpcomingSchedule)
--    Use when you want to compare consult vs echo/stress volume.
-- -----------------------------------------------------------------------------
SELECT
    P.PAT_ID,
    P.PAT_FIRSTNAME,
    P.PAT_LASTNAME,
    A.APPOINTMENT_DATE,
    A.APPOINTMENT_TIME,
    S.OCC_SERVICE_TITLE,
    TRIM(NVL(ST.SITE_NAME, ' ')) AS SITE_NAME,
    P.PAT_DOB,
    P.PAT_MOB_PHONE,
    P.PAT_CARDIOLOGIST,
    A.APPOINTMENT_NOTE,
    A.SCHEDULE_APPOINTMENT_RID
FROM HEARTS1ST.SCHEDULE_APPOINTMENT A
JOIN HEARTS1ST.PATIENT P
  ON P.PATIENT_RID = A.PATIENT_RID
LEFT JOIN HEARTS1ST.SCHEDULE_OCC_SERVICE S
  ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
LEFT JOIN HEARTS1ST.SCHEDULE_ROOM R
  ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
LEFT JOIN HEARTS1ST.SCHEDULE_SITE ST
  ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
WHERE A.APPOINTMENT_DATE >= TO_DATE(:startDate, 'YYYY-MM-DD')
  AND A.APPOINTMENT_DATE < TO_DATE(:endDate, 'YYYY-MM-DD') + 1
  AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
ORDER BY A.APPOINTMENT_DATE, A.APPOINTMENT_TIME;


-- -----------------------------------------------------------------------------
-- 6) Prior completed consult letters for a booked patient (screening text source)
--    CONSULT ≠ SCHEDULE_APPOINTMENT. Use for post-consult RTF path later.
-- -----------------------------------------------------------------------------
SELECT
    P.PAT_ID,
    L.LETTER_DATE,
    C.USER_LIST_RID AS reporting_cardiologist_rid,
    C.CONSULT_RID
FROM HEARTS1ST.CONSULT C
JOIN HEARTS1ST.LETTERS L
  ON L.LETTERS_RID = C.LETTERS_RID
JOIN HEARTS1ST.PATIENT P
  ON P.PATIENT_RID = C.PATIENT_RID   -- verify column if ORA-00904
WHERE P.PAT_ID = :patId
ORDER BY L.LETTER_DATE DESC
FETCH FIRST 10 ROWS ONLY;
