using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>R1.5b b4 第 4 部分：Data/Source/Courses.json → CourseDef（COURSE_periodic／daily）、備品架排列（Resources/SpareArrangement）、程序的 onPass。</summary>
    public static partial class StepsCsvImporter
    {
        public static string CoursesPath => SourceFolder + "/Courses.json";
        public static string CourseDefPath(string course) => $"{CourseFolder}/COURSE_{course}.asset";
        public const string SpareArrangementPath = "Assets/_Project/Modules/Worksite/Resources/SpareArrangement.asset";

        [Serializable]
        public class CourseJson
        {
            public string courseId, title;
            public CourseDef.Placement[] placements = Array.Empty<CourseDef.Placement>();
            public JumpRule[] onPass = Array.Empty<JumpRule>();
        }

        [Serializable]
        public class CoursesFile
        {
            public CourseJson[] courses = Array.Empty<CourseJson>();
            public SpareArrangementDef.Entry[] spareArrangement = Array.Empty<SpareArrangementDef.Entry>();
        }

        public static CoursesFile LoadCourses() => JsonUtility.FromJson<CoursesFile>(File.ReadAllText(CoursesPath, Encoding.UTF8));

        /// <summary>課程資料的檢查：每門課都有；擺放的 ID（name: 以外）在場景裡；onPass 的步驟在該課程裡。</summary>
        public static void CheckCourses(CoursesFile f, ICollection<string> sceneIds, Dictionary<string, List<StepDefinition>> courses, List<string> errors)
        {
            foreach (var c in Courses)
            {
                var cj = f.courses.FirstOrDefault(x => x.courseId == c);
                if (cj == null) { errors.Add($"Courses.json 沒有 {c}"); continue; }
                foreach (var p in cj.placements.Where(p => !p.id.StartsWith("name:") && sceneIds.Count > 0 && !sceneIds.Contains(p.id)))
                    errors.Add($"Courses.json {c}：擺放的 {p.id} 不在場景");
                var ids = new HashSet<string>(courses[c].Select(s => s.stepId));
                foreach (var j in cj.onPass.Where(j => !ids.Contains(j.stepId) || !ids.Contains(j.jumpTo)))
                    errors.Add($"Courses.json {c}：onPass {j.stepId} → {j.jumpTo} 的步驟不在這門課");
            }
            foreach (var side in new[] { "L", "R" })
                if (f.spareArrangement.Count(e => e.side == side && e.model == SpareModelCorrect && !e.cracked) != 1)
                    errors.Add($"Courses.json spareArrangement {side}：正確型號而且沒有裂紋的要剛好 1 條");
        }

        public const string SpareModelCorrect = "CS-1100-74A";   // 和 SpareShelf.CorrectModel 相同（作業單要求的型號）
    }
}
