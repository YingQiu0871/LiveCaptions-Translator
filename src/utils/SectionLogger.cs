using Microsoft.Data.Sqlite;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // Stores lecture sections (time range + summary) next to the translation history.
    // Uses its own connections so that the summarizer never contends with the caption loops.
    public static class SectionLogger
    {
        static SectionLogger()
        {
            using var connection = Open();
            using var command = new SqliteCommand(@"
                CREATE TABLE IF NOT EXISTS SectionSummary (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime INTEGER,
                    EndTime INTEGER,
                    FirstHistoryId INTEGER,
                    LastHistoryId INTEGER,
                    Summary TEXT,
                    PageNumber INTEGER
                );
                CREATE TABLE IF NOT EXISTS Paragraph (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Time INTEGER,
                    FirstHistoryId INTEGER,
                    LastHistoryId INTEGER,
                    Source TEXT,
                    Translation TEXT
                );", connection);
            command.ExecuteNonQuery();
        }

        // Creates the tables (in the static constructor) before another class queries them.
        public static void EnsureTables()
        {
        }

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(SQLiteHistoryLogger.CONNECTION_STRING);
            connection.Open();
            return connection;
        }

        private static DateTime ToLocal(object unixTime) =>
            DateTimeOffset.FromUnixTimeSeconds((long)Convert.ToDouble(unixTime)).LocalDateTime;

        public static async Task<long> GetMaxHistoryId(CancellationToken token = default)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(
                "SELECT IFNULL(MAX(Id), 0) FROM TranslationHistory", connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync(token));
        }

        public static async Task<List<HistoryLine>> LoadHistoryRange(long afterId, long upToId = long.MaxValue,
            CancellationToken token = default)
        {
            var lines = new List<HistoryLine>();
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT Id, Timestamp, SourceText, TranslatedText
                FROM TranslationHistory
                WHERE Id > @afterId AND Id <= @upToId
                ORDER BY Id ASC", connection);
            command.Parameters.AddWithValue("@afterId", afterId);
            command.Parameters.AddWithValue("@upToId", upToId);

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                DateTime time;
                try
                {
                    time = ToLocal(reader.GetValue(1));
                }
                catch (FormatException)
                {
                    continue;
                }
                lines.Add(new HistoryLine
                {
                    Id = reader.GetInt64(0),
                    Time = time,
                    SourceText = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    TranslatedText = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                });
            }
            return lines;
        }

        public static async Task<SectionEntry> AddSection(DateTime start, DateTime end,
            long firstHistoryId, long lastHistoryId, string summary, int? pageNumber,
            CancellationToken token = default)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                INSERT INTO SectionSummary (StartTime, EndTime, FirstHistoryId, LastHistoryId, Summary, PageNumber)
                VALUES (@start, @end, @first, @last, @summary, @page);
                SELECT last_insert_rowid();", connection);
            command.Parameters.AddWithValue("@start", new DateTimeOffset(start).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("@end", new DateTimeOffset(end).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("@first", firstHistoryId);
            command.Parameters.AddWithValue("@last", lastHistoryId);
            command.Parameters.AddWithValue("@summary", summary);
            command.Parameters.AddWithValue("@page", pageNumber.HasValue ? pageNumber.Value : DBNull.Value);
            long id = Convert.ToInt64(await command.ExecuteScalarAsync(token));

            return new SectionEntry
            {
                Id = id,
                StartTime = start,
                EndTime = end,
                FirstHistoryId = firstHistoryId,
                LastHistoryId = lastHistoryId,
                Summary = summary,
                PageNumber = pageNumber,
            };
        }

        private static SectionEntry ReadSection(System.Data.Common.DbDataReader reader)
        {
            return new SectionEntry
            {
                Id = reader.GetInt64(0),
                StartTime = ToLocal(reader.GetValue(1)),
                EndTime = ToLocal(reader.GetValue(2)),
                FirstHistoryId = reader.GetInt64(3),
                LastHistoryId = reader.GetInt64(4),
                Summary = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                PageNumber = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            };
        }

        // The sections that start within the history rows (afterId, upToId].
        public static async Task<List<SectionEntry>> LoadSectionsInRange(long afterId, long upToId,
            CancellationToken token = default)
        {
            var sections = new List<SectionEntry>();
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT Id, StartTime, EndTime, FirstHistoryId, LastHistoryId, Summary, PageNumber
                FROM SectionSummary
                WHERE FirstHistoryId > @after AND FirstHistoryId <= @upTo
                ORDER BY FirstHistoryId ASC", connection);
            command.Parameters.AddWithValue("@after", afterId);
            command.Parameters.AddWithValue("@upTo", upToId);

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                sections.Add(ReadSection(reader));
            }
            return sections;
        }

        public static async Task<List<SectionEntry>> LoadSections(DateTime day, CancellationToken token = default)
        {
            var sections = new List<SectionEntry>();
            long from = new DateTimeOffset(day.Date).ToUnixTimeSeconds();
            long to = new DateTimeOffset(day.Date.AddDays(1)).ToUnixTimeSeconds();

            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT Id, StartTime, EndTime, FirstHistoryId, LastHistoryId, Summary, PageNumber
                FROM SectionSummary
                WHERE StartTime >= @from AND StartTime < @to
                ORDER BY StartTime ASC", connection);
            command.Parameters.AddWithValue("@from", from);
            command.Parameters.AddWithValue("@to", to);

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                sections.Add(ReadSection(reader));
            }
            return sections;
        }

        // A few raw sentences rewritten by the LLM with context: recognition errors fixed, translated as a whole.
        public static async Task AddParagraph(DateTime time, long firstHistoryId, long lastHistoryId,
            string source, string translation, CancellationToken token = default)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                INSERT INTO Paragraph (Time, FirstHistoryId, LastHistoryId, Source, Translation)
                VALUES (@time, @first, @last, @source, @translation);", connection);
            command.Parameters.AddWithValue("@time", new DateTimeOffset(time).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("@first", firstHistoryId);
            command.Parameters.AddWithValue("@last", lastHistoryId);
            command.Parameters.AddWithValue("@source", source);
            command.Parameters.AddWithValue("@translation", translation);
            await command.ExecuteNonQueryAsync(token);
        }

        public static async Task<List<ParagraphEntry>> LoadParagraphs(long afterHistoryId,
            CancellationToken token = default)
        {
            var paragraphs = new List<ParagraphEntry>();
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT Time, FirstHistoryId, LastHistoryId, Source, Translation
                FROM Paragraph
                WHERE LastHistoryId > @after
                ORDER BY FirstHistoryId ASC", connection);
            command.Parameters.AddWithValue("@after", afterHistoryId);

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                paragraphs.Add(new ParagraphEntry
                {
                    Time = ToLocal(reader.GetValue(0)),
                    FirstHistoryId = reader.GetInt64(1),
                    LastHistoryId = reader.GetInt64(2),
                    Source = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Translation = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                });
            }
            return paragraphs;
        }

        public static async Task ClearSections(CancellationToken token = default)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(
                "DELETE FROM SectionSummary; DELETE FROM sqlite_sequence WHERE NAME='SectionSummary'; " +
                "DELETE FROM Paragraph; DELETE FROM sqlite_sequence WHERE NAME='Paragraph'", connection);
            await command.ExecuteNonQueryAsync(token);
        }
    }
}
