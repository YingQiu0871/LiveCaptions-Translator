using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // The save flow after a recording stops. The dialogs it asks with (LectureDialogs.AskName) differ per platform.
    public static class LectureSaving
    {
        // After "停止": name the recording, then save it once its last section is summarized.
        public static async Task SaveAfterStop(long lectureId)
        {
            if (lectureId < 0)
                return;
            var finishing = ClassSession.FinishLecture(lectureId);

            try
            {
                var lecture = await LectureStore.GetLecture(lectureId);
                if (lecture != null)
                {
                    var answer = await LectureDialogs.AskName("保存本节课", lecture.CourseName, lecture.Name, "保存", "用默认名称");
                    if (answer is { } chosen)
                    {
                        await LectureStore.RenameLecture(lectureId, chosen.Name, chosen.Course);
                        Translator.Setting.Lecture.CurrentCourse = chosen.Course;
                    }
                }

                await finishing;
                lecture = await LectureStore.GetLecture(lectureId);
                if (lecture == null)
                    return;
                string path = await LectureDocument.Save(lecture);
                SnackbarHost.Show("本节课已保存。", path, SnackbarType.Success, timeout: 3);
                if (Translator.Setting.Lecture.NotesDraft)
                    await WriteNotesDraft(lecture);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 保存本节课失败。", ex.Message, SnackbarType.Error, timeout: 3, closeButton: true);
            }
        }

        public static async Task<string?> WriteNotesDraft(LectureRecord lecture)
        {
            SnackbarHost.Show("正在生成笔记初稿……", "整节课的内容较多，大约需要半分钟到一两分钟。", SnackbarType.Info, timeout: 3);
            try
            {
                string path = await LectureNotes.Generate(lecture);
                SnackbarHost.Show("笔记初稿已生成。", path, SnackbarType.Success, timeout: 3);
                return path;
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 笔记初稿生成失败。", ex.Message, SnackbarType.Error, timeout: 3, closeButton: true);
                return null;
            }
        }
    }
}
