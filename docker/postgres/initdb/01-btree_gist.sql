-- Habilita la extensión GiST de árbol B para el operador de igualdad en
-- "excludes", requerida por la restricción EXCLUDE de la tabla turnos (§4.1).
-- Se ejecuta una sola vez al inicializar el volumen (docker-entrypoint-initdb.d).
CREATE EXTENSION IF NOT EXISTS btree_gist;