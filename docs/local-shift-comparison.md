# All 51 shift definitions — compatible upgrade comparison

Source: the verified regular `shiftmanager` backup, restored into isolated RAM-backed
PostgreSQL. The original database was not migrated. Every value below is **before = after**.
All existing columns, including `UpdatedAt`, were also compared programmatically and match.
The full before/after JSON is retained in the restricted validation directory referenced
by [the results](local-migration-results.md).

Every ID is `40000000-0000-0000-0000-` plus the complete 12-digit suffix shown below.
For **all 51 rows**, the previously absent `AssignmentMode` becomes `Automatic` and the
previously absent `SchedulingPriority` becomes `Normal`. The existing automatic flag
continues to decide inclusion. There is no classification swap or version bump and no
critical priority assigned by legacy ID. Existing label/time inconsistencies are preserved
rather than silently repaired.

| ID suffix | Name before = after | Order | Staffing | Automatic flag | Period / time range | Qualified required | Semantic | Version |
| --- | --- | ---: | ---: | --- | --- | --- | --- | ---: |
| 000000000001 | 6 עד 9 | 1 | 1 | true | DAY/H06_09 | false | SIX_NINE | 4 |
| 000000000002 | א׳ מומחה | 2 | 1 | true | DAY/DAY | true | ORDINARY | 4 |
| 000000000003 | א׳ בוקר | 3 | 3 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000004 | א׳ ערב מומחה | 4 | 0 | false | DAY/H15_23 | false | ORDINARY | 3 |
| 000000000005 | א׳ ערב כונן | 5 | 0 | false | DAY/H15_23 | false | ORDINARY | 3 |
| 000000000006 | א׳ ערב נעילה | 6 | 0 | false | DAY/H15_23 | false | ORDINARY | 3 |
| 000000000007 | א׳ ערב קצרה | 7 | 0 | false | DAY/H15_23 | false | ORDINARY | 3 |
| 000000000008 | עד 16 + אריאל ערב | 8 | 0 | false | DAY/DAY | false | ORDINARY | 2 |
| 000000000009 | א׳ לילה מומחה | 9 | 1 | true | NIGHT/H23_07 | true | ORDINARY | 5 |
| 000000000010 | א׳ לילה | 10 | 1 | true | NIGHT/H23_07 | false | ORDINARY | 4 |
| 000000000011 | ב׳ בוקר מומחה | 11 | 1 | true | DAY/DAY | true | ORDINARY | 4 |
| 000000000012 | ב׳ בוקר | 12 | 1 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000013 | ב׳ לילה מומחה | 13 | 1 | true | DAY/DAY | true | ORDINARY | 3 |
| 000000000014 | ב׳ לילה | 14 | 1 | true | NIGHT/NIGHT | false | ORDINARY | 4 |
| 000000000015 | שיקום בוקר | 15 | 1 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000016 | שיקום ערב | 16 | 0 | true | DAY/H15_23 | false | ORDINARY | 4 |
| 000000000017 | כונן פנימית | 17 | 0 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000018 | מרדים יום שלם | 18 | 0 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000019 | מומחה יום שלם | 19 | 6 | true | DAY/DAY | true | ORDINARY | 4 |
| 000000000020 | מומחה עד 16 | 20 | 0 | true | DAY/H07_15 | false | ORDINARY | 4 |
| 000000000021 | יום שלם | 21 | 10 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000022 | כונן עד 16 | 22 | 5 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000023 | כונן עד 13 | 23 | 2 | true | DAY/H07_13 | false | ORDINARY | 4 |
| 000000000024 | כונן ערב | 24 | 2 | true | DAY/H15_23 | false | ORDINARY | 4 |
| 000000000025 | כונן לילה | 25 | 6 | true | NIGHT/NIGHT | false | ORDINARY | 3 |
| 000000000026 | כונן סופש | 26 | 4 | true | DAY/DAY | false | WEEKEND_ON_CALL | 3 |
| 000000000027 | אימון בוקר | 27 | 6 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000028 | אימון יום שלם | 28 | 0 | false | DAY/DAY | false | ORDINARY | 2 |
| 000000000029 | אימון ערב | 29 | 0 | false | DAY/H15_23 | false | ORDINARY | 3 |
| 000000000030 | הדרכה בוקר | 30 | 10 | true | DAY/DAY | false | ORDINARY | 3 |
| 000000000031 | משמרת 31 | 31 | 0 | false | DAY/DAY | false | ORDINARY | 4 |
| 000000000032 | משמרת 32 | 32 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000033 | משמרת 33 | 33 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000034 | משמרת 34 | 34 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000035 | משמרת 35 | 35 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000036 | משמרת 36 | 36 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000037 | משמרת 37 | 37 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000038 | משמרת 38 | 38 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000039 | משמרת 39 | 39 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000040 | משמרת 40 | 40 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000041 | משמרת 41 | 41 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000042 | משמרת 42 | 42 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000043 | משמרת 43 | 43 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000044 | משמרת 44 | 44 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000045 | משמרת 45 | 45 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000046 | משמרת 46 | 46 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000047 | משמרת 47 | 47 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000048 | משמרת 48 | 48 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000049 | משמרת 49 | 49 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000050 | משמרת 50 | 50 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
| 000000000051 | משמרת 51 | 51 | 0 | false | DAY/DAY | false | ORDINARY | 1 |
