-- Report BI menu for a fresh database (the menu is not part of the seed data).
-- Run AFTER the backend has finished its first start, so sysmenu exists.
-- Safe to run repeatedly.

SET client_encoding = 'UTF8';

-- Menu entry (sysmenu)
INSERT INTO sysmenu (id, pid, type, title, name, path, component, icon, isiframe, ishide, iskeepalive, isaffix, orderno, status, createtime)
VALUES (1311000011101, 1311000000101, 2, '报表BI', 'iotReport', '/iot/report', '/iot/report/index', 'ele-Document', false, false, true, false, 155, 1, now())
ON CONFLICT (id) DO NOTHING;

-- Tenant binding (systenantmenu, default tenant 1300000000001)
INSERT INTO systenantmenu (id, tenantid, menuid)
VALUES (1309000011101, 1300000000001, 1311000011101)
ON CONFLICT (id) DO NOTHING;
