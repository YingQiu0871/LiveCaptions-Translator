using Microsoft.Data.Sqlite;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // Courses and their recordings ("lectures"). A lecture does not copy the sentences: it remembers which history
    // rows belong to it, and the sections and refined paragraphs are found by the same history ids.
    public static class LectureStore
    {
        public const string DEFAULT_COURSE = "未分类";
        public const string OLD_RECORDS_COURSE = "以前的记录";

        static LectureStore()
        {
            using var connection = Open();
            bool existed;
            using (var check = new SqliteCommand(
                       "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Lecture'", connection))
            {
                existed = Convert.ToInt64(check.ExecuteScalar()) > 0;
            }

            using (var command = new SqliteCommand(@"
                CREATE TABLE IF NOT EXISTS Course (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    Created INTEGER
                );
                CREATE TABLE IF NOT EXISTS Lecture (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CourseId INTEGER NOT NULL,
                    Name TEXT,
                    StartTime INTEGER,
                    EndTime INTEGER,
                    FirstHistoryId INTEGER,
                    LastHistoryId INTEGER,
                    FilePath TEXT
                );", connection))
            {
                command.ExecuteNonQuery();
            }

            if (!existed)
            {
                try
                {
                    ImportOldHistory(connection);
                }
                catch (Exception)
                {
                    // The old sentences stay available in the "全部句子" list.
                }
            }
        }

        private static SqliteConnection Open()
        {
            // Make sure the history, section and paragraph tables exist before they are queried.
            SectionLogger.EnsureTables();
            var connection = new SqliteConnection(SQLiteHistoryLogger.CONNECTION_STRING);
            connection.Open();
            return connection;
        }

        private static long Now() => DateTimeOffset.Now.ToUnixTimeSeconds();
        private static long ToUnix(DateTime time) => new DateTimeOffset(time).ToUnixTimeSeconds();
        private static DateTime ToLocal(long unixTime) => DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;

        // Recordings made before courses existed: one lecture per day in the course "以前的记录".
        private static void ImportOldHistory(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            var days = new List<(long First, long Last, long Start, long End)>();
            using (var command = new SqliteCommand(@"
                SELECT MIN(Id), MAX(Id), MIN(CAST(Timestamp AS INTEGER)), MAX(CAST(Timestamp AS INTEGER))
                FROM TranslationHistory
                WHERE Timestamp GLOB '[0-9]*'
                GROUP BY date(CAST(Timestamp AS INTEGER), 'unixepoch', 'localtime')
                ORDER BY MIN(Id)", connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    days.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
            }
            if (days.Count == 0)
            {
                transaction.Commit();
                return;
            }

            long courseId = EnsureCourse(connection, transaction, OLD_RECORDS_COURSE);
            foreach (var day in days)
            {
                using var insert = new SqliteCommand(@"
                    INSERT INTO Lecture (CourseId, Name, StartTime, EndTime, FirstHistoryId, LastHistoryId, FilePath)
                    VALUES (@course, @name, @start, @end, @first, @last, '')", connection, transaction);
                insert.Parameters.AddWithValue("@course", courseId);
                insert.Parameters.AddWithValue("@name", $"{ToLocal(day.Start):yyyy-MM-dd} 的记录");
                insert.Parameters.AddWithValue("@start", day.Start);
                insert.Parameters.AddWithValue("@end", day.End);
                insert.Parameters.AddWithValue("@first", day.First - 1);
                insert.Parameters.AddWithValue("@last", day.Last);
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        private static long EnsureCourse(SqliteConnection connection, SqliteTransaction? transaction, string name)
        {
            using (var insert = new SqliteCommand(
                       "INSERT OR IGNORE INTO Course (Name, Created) VALUES (@name, @now)", connection, transaction))
            {
                insert.Parameters.AddWithValue("@name", name);
                insert.Parameters.AddWithValue("@now", Now());
                insert.ExecuteNonQuery();
            }
            using var select = new SqliteCommand("SELECT Id FROM Course WHERE Name = @name", connection, transaction);
            select.Parameters.AddWithValue("@name", name);
            return Convert.ToInt64(select.ExecuteScalar());
        }

        public static string CleanCourseName(string? name)
        {
            name = (name ?? string.Empty).Trim();
            return name.Length == 0 ? DEFAULT_COURSE : name;
        }

        public static Task<long> AddCourse(string name) => Task.Run(() =>
        {
            using var connection = Open();
            return EnsureCourse(connection, null, CleanCourseName(name));
        });

        public static async Task<List<CourseEntry>> LoadCourses()
        {
            var courses = new List<CourseEntry>();
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT c.Id, c.Name, COUNT(l.Id), MAX(l.StartTime)
                FROM Course c LEFT JOIN Lecture l ON l.CourseId = c.Id
                GROUP BY c.Id
                ORDER BY IFNULL(MAX(l.StartTime), c.Created) DESC", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                courses.Add(new CourseEntry
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    LectureCount = reader.GetInt32(2),
                    LastTime = reader.IsDBNull(3) ? null : ToLocal(reader.GetInt64(3)),
                });
            }
            return courses;
        }

        public static async Task<List<LectureRecord>> LoadLectures(long courseId)
        {
            var lectures = new List<LectureRecord>();
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT l.Id, l.CourseId, c.Name, l.Name, l.StartTime, l.EndTime, l.FirstHistoryId, l.LastHistoryId,
                       l.FilePath,
                       (SELECT COUNT(*) FROM TranslationHistory h
                        WHERE h.Id > l.FirstHistoryId AND h.Id <= IFNULL(l.LastHistoryId,
                            IFNULL((SELECT MIN(n.FirstHistoryId) FROM Lecture n WHERE n.FirstHistoryId > l.FirstHistoryId),
                                   9223372036854775807)))
                FROM Lecture l JOIN Course c ON c.Id = l.CourseId
                WHERE l.CourseId = @course
                ORDER BY l.StartTime DESC", connection);
            command.Parameters.AddWithValue("@course", courseId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lectures.Add(ReadLecture(reader));
            return lectures;
        }

        public static async Task<LectureRecord?> GetLecture(long id)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT l.Id, l.CourseId, c.Name, l.Name, l.StartTime, l.EndTime, l.FirstHistoryId, l.LastHistoryId,
                       l.FilePath, 0
                FROM Lecture l JOIN Course c ON c.Id = l.CourseId
                WHERE l.Id = @id", connection);
            command.Parameters.AddWithValue("@id", id);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadLecture(reader) : null;
        }

        private static LectureRecord ReadLecture(SqliteDataReader reader) => new()
        {
            Id = reader.GetInt64(0),
            CourseId = reader.GetInt64(1),
            CourseName = reader.GetString(2),
            Name = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            StartTime = ToLocal(reader.GetInt64(4)),
            EndTime = reader.IsDBNull(5) ? null : ToLocal(reader.GetInt64(5)),
            FirstHistoryId = reader.GetInt64(6),
            LastHistoryId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            FilePath = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            SentenceCount = reader.GetInt32(9),
        };

        // The last history row of a lecture. One that was never stopped properly ends where the next one begins.
        public static async Task<long> EffectiveLastHistoryId(LectureRecord lecture)
        {
            if (lecture.LastHistoryId is long last)
                return last;
            if (ClassSession.IsRunning && ClassSession.CurrentLectureId == lecture.Id)
                return long.MaxValue;
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                SELECT IFNULL((SELECT MIN(FirstHistoryId) FROM Lecture WHERE FirstHistoryId > @first),
                              (SELECT IFNULL(MAX(Id), 0) FROM TranslationHistory))", connection);
            command.Parameters.AddWithValue("@first", lecture.FirstHistoryId);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public static async Task<long> StartLecture(string courseName, string name, DateTime start, long firstHistoryId)
        {
            long courseId = await AddCourse(courseName);
            await using var connection = Open();
            await using var command = new SqliteCommand(@"
                INSERT INTO Lecture (CourseId, Name, StartTime, FirstHistoryId, FilePath)
                VALUES (@course, @name, @start, @first, '');
                SELECT last_insert_rowid();", connection);
            command.Parameters.AddWithValue("@course", courseId);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@start", ToUnix(start));
            command.Parameters.AddWithValue("@first", firstHistoryId);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public static async Task FinishLecture(long id, DateTime end, long lastHistoryId)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(
                "UPDATE Lecture SET EndTime = @end, LastHistoryId = @last WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@end", ToUnix(end));
            command.Parameters.AddWithValue("@last", lastHistoryId);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public static async Task RenameLecture(long id, string name, string courseName)
        {
            long courseId = await AddCourse(courseName);
            await using var connection = Open();
            await using var command = new SqliteCommand(
                "UPDATE Lecture SET Name = @name, CourseId = @course WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@course", courseId);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public static async Task SetFilePath(long id, string path)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand("UPDATE Lecture SET FilePath = @path WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@path", path);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        // Removes the lecture together with its sentences, sections and paragraphs.
        public static async Task DeleteLecture(LectureRecord lecture)
        {
            long last = await EffectiveLastHistoryId(lecture);
            await using var connection = Open();
            await using var transaction = connection.BeginTransaction();
            foreach (string sql in new[]
                     {
                         "DELETE FROM TranslationHistory WHERE Id > @first AND Id <= @last",
                         "DELETE FROM SectionSummary WHERE FirstHistoryId > @first AND FirstHistoryId <= @last",
                         "DELETE FROM Paragraph WHERE FirstHistoryId > @first AND FirstHistoryId <= @last",
                         "DELETE FROM Lecture WHERE Id = @id",
                     })
            {
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@first", lecture.FirstHistoryId);
                command.Parameters.AddWithValue("@last", last);
                command.Parameters.AddWithValue("@id", lecture.Id);
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        public static async Task RenameCourse(long id, string name)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand("UPDATE Course SET Name = @name WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@name", CleanCourseName(name));
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        // Only empty courses are deleted: recordings are deleted one by one.
        public static async Task<bool> DeleteCourse(long id)
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(
                "DELETE FROM Course WHERE Id = @id AND NOT EXISTS (SELECT 1 FROM Lecture WHERE CourseId = @id)",
                connection);
            command.Parameters.AddWithValue("@id", id);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        // After "全部删除": history ids restart from 1, so the lectures can no longer point at them.
        public static async Task ClearLectures()
        {
            await using var connection = Open();
            await using var command = new SqliteCommand(
                "DELETE FROM Lecture; DELETE FROM sqlite_sequence WHERE NAME='Lecture'", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
