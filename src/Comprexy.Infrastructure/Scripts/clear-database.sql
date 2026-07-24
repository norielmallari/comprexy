-- Drops all Comprexy application tables (including EF migration history) so the next
-- API start / Migrate can recreate schema from scratch.
-- SQLite-compatible. Prefer the CLI when the API would otherwise lock the file:
--   dotnet run --project apps/proxy -- --clear-db
--
-- Manual (from the directory that contains comprexy.db, typically repo data/):
--   sqlite3 comprexy.db < src/Comprexy.Infrastructure/Scripts/clear-database.sql
-- Then run the proxy or control-api once so Migrate recreates tables.

PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

DROP TABLE IF EXISTS CompressionEvents;
DROP TABLE IF EXISTS ConversationMessages;
DROP TABLE IF EXISTS WorkingMemories;
DROP TABLE IF EXISTS Conversations;
DROP TABLE IF EXISTS __EFMigrationsHistory;

COMMIT;

PRAGMA foreign_keys = ON;
