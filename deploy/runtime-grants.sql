-- Ejecutar como propietario, conectado explícitamente a la base de despliegue.
-- Crear previamente helpdesk_runtime como LOGIN con secreto externo, sin privilegios elevados.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO helpdesk_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    public."Users", public."Tickets", public."TicketComments", public."TicketActivities"
    TO helpdesk_runtime;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO helpdesk_runtime;
-- Sin ownership, CREATE, TRUNCATE ni acceso a __EFMigrationsHistory.
-- Revisar explícitamente los grants al incorporar tablas futuras.
