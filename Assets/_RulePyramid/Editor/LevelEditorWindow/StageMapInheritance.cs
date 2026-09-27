using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RulePyramid.Core;
using RulePyramid.Runtime;

namespace RulePyramid.Editor
{
    // 只负责作者初态。运行时继续读取完整地图，不解析继承关系。
    public static class StageMapInheritance
    {
        const string Metadata = "editorInheritance";
        static JObject Json(LevelDefinition map) => JObject.Parse(LevelJsonSerializer.ToJson(map));
        public static string Parent(LevelDefinition map) => (string)Json(map)[Metadata]?["parent"];

        static void ValidateBounds(LevelDefinition map)
        {
            if (!MapResize.TryValidateBounds(map.bounds, out var error)) throw new InvalidOperationException(error);
            foreach (var box in map.terrain ?? Array.Empty<GridCellBox>())
                if (box == null || box.min.x > box.max.x || box.min.y > box.max.y || box.min.z > box.max.z
                    || !AuthoringOperations.Contains(map, box.min) || !AuthoringOperations.Contains(map, box.max))
                    throw new InvalidOperationException("地形越界或范围无效：" + box?.id);
            foreach (var entity in map.entities ?? Array.Empty<EntityDefinition>())
                if (entity == null || !AuthoringOperations.Contains(map, entity.cell))
                    throw new InvalidOperationException("实体越界：" + entity?.id);
        }

        // 按实体 ID/字段与地形格记录覆盖，不依赖可被切割重编号的 terrain ID。
        static JObject Layout(JObject map)
        {
            var result = new JObject { ["bounds"] = map["bounds"].DeepClone() };
            var entities = new JObject();
            foreach (var entity in (JArray)map["entities"])
                entities[(string)entity["id"]] = entity.DeepClone();
            result["entities"] = entities;
            var terrain = new JObject();
            foreach (var box in (JArray)map["terrain"])
                for (int x = (int)box["min"]["x"]; x <= (int)box["max"]["x"]; x++)
                    for (int y = (int)box["min"]["y"]; y <= (int)box["max"]["y"]; y++)
                        for (int z = (int)box["min"]["z"]; z <= (int)box["max"]["z"]; z++)
                            terrain[x + "," + y + "," + z] = (string)box["appearance"] ?? "Stone";
            result["terrain"] = terrain;
            return result;
        }

        static void Diff(JToken before, JToken after, List<string> path, JArray changes)
        {
            if (JToken.DeepEquals(before, after)) return;
            if (before is JObject a && after is JObject b)
            {
                foreach (string key in a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name)))
                    Diff(a[key], b[key], path.Concat(new[] { key }).ToList(), changes);
                return;
            }
            changes.Add(new JObject { ["path"] = new JArray(path), ["remove"] = after == null, ["value"] = after?.DeepClone() });
        }

        static JObject Apply(JObject basis, JArray changes)
        {
            var result = (JObject)basis.DeepClone();
            foreach (var change in changes)
            {
                string[] path = change["path"].Values<string>().ToArray();
                JObject cursor = result;
                for (int i = 0; i < path.Length - 1; i++)
                {
                    if (!(cursor[path[i]] is JObject)) cursor[path[i]] = new JObject();
                    cursor = (JObject)cursor[path[i]];
                }
                if ((bool)change["remove"]) cursor.Remove(path.Last());
                else cursor[path.Last()] = change["value"].DeepClone();
            }
            return result;
        }

        static void Materialize(JObject map, JObject layout)
        {
            map["bounds"] = layout["bounds"].DeepClone();
            map["entities"] = new JArray(((JObject)layout["entities"]).Properties().Select(p => p.Value.DeepClone()));
            // 保留原地形分组（没有地形变化时）；有变化时沿 X 合并，避免每格一个长方体。
            if (JToken.DeepEquals(Layout(map)["terrain"], layout["terrain"])) return;
            var cells = ((JObject)layout["terrain"]).Properties().Select(p => new { Position = p.Name.Split(',').Select(int.Parse).ToArray(), Appearance = (string)p.Value });
            var boxes = new JArray();
            foreach (var row in cells.GroupBy(c => new { Y = c.Position[1], Z = c.Position[2], c.Appearance }).OrderBy(g => g.Key.Y).ThenBy(g => g.Key.Z).ThenBy(g => g.Key.Appearance))
            {
                int[] xs = row.Select(c => c.Position[0]).OrderBy(x => x).ToArray();
                for (int i = 0; i < xs.Length; i++)
                {
                    int start = xs[i], end = start;
                    while (i + 1 < xs.Length && xs[i + 1] == end + 1) end = xs[++i];
                    boxes.Add(new JObject { ["id"] = "inherited_" + boxes.Count, ["appearance"] = row.Key.Appearance,
                        ["min"] = new JObject { ["x"] = start, ["y"] = row.Key.Y, ["z"] = row.Key.Z },
                        ["max"] = new JObject { ["x"] = end, ["y"] = row.Key.Y, ["z"] = row.Key.Z } });
                }
            }
            map["terrain"] = boxes;
        }

        public static LevelDefinition Link(LevelDefinition parent, LevelDefinition child, string parentFile)
        {
            ValidateBounds(parent); ValidateBounds(child);
            var json = Json(child);
            var layout = Layout(json);
            var changes = new JArray();
            Diff(Layout(Json(parent)), layout, new List<string>(), changes);
            json[Metadata] = new JObject { ["version"] = 1, ["parent"] = parentFile, ["overrides"] = changes, ["resolved"] = layout };
            return LevelJsonSerializer.FromDraftJson(json.ToString());
        }

        public static LevelDefinition Resolve(LevelDefinition parent, LevelDefinition child)
        {
            ValidateBounds(parent); ValidateBounds(child);
            var json = Json(child);
            var meta = json[Metadata] as JObject ?? throw new InvalidOperationException("尚未建立阶段继承");
            if ((int?)meta["version"] != 1) throw new InvalidOperationException("不支持的阶段继承版本");
            var local = Layout(json);
            var changes = (JArray)meta["overrides"];
            // 只追加本轮真正编辑的字段；覆盖即使暂时等于父阶段也不会丢失。
            Diff(meta["resolved"], local, new List<string>(), changes);
            var resolved = Apply(Layout(Json(parent)), changes);
            // 父阶段删除了子阶段改过的实体时，保留完整的子阶段实体，避免残缺字段。
            var entities = (JObject)resolved["entities"];
            foreach (var property in entities.Properties().ToArray())
                if (property.Value["id"] == null && local["entities"][property.Name] != null)
                    entities[property.Name] = local["entities"][property.Name].DeepClone();
            Materialize(json, resolved);
            meta["resolved"] = resolved;
            if (!JToken.DeepEquals(local, resolved))
                foreach (var solution in (JArray)json["referenceSolutions"]) solution["status"] = "布局继承后待重新验证";
            return LevelJsonSerializer.FromDraftJson(json.ToString());
        }

        public static Dictionary<string, LevelDefinition> BuildSave(string path, LevelDefinition draft, string[] sequence)
        {
            path = Path.GetFullPath(path);
            var maps = sequence.Select(Path.GetFullPath).ToDictionary(p => p, p => LevelJsonSerializer.FromDraftJson(File.ReadAllText(p, Encoding.UTF8)), StringComparer.OrdinalIgnoreCase);
            // 建立关系前留下的撤销记录没有元数据，不应因此静默解除继承。
            if (maps.TryGetValue(path, out var disk) && string.IsNullOrEmpty(Parent(draft)) && !string.IsNullOrEmpty(Parent(disk)))
            {
                var json = Json(draft);
                json[Metadata] = Json(disk)[Metadata].DeepClone();
                draft = LevelJsonSerializer.FromDraftJson(json.ToString());
            }
            maps[path] = draft;
            var output = new Dictionary<string, LevelDefinition>(StringComparer.OrdinalIgnoreCase);
            bool affected = false;
            foreach (string entry in sequence.Select(Path.GetFullPath))
            {
                string parentFile = Parent(maps[entry]);
                string parentPath = string.IsNullOrEmpty(parentFile) ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry), parentFile));
                if (parentPath != null && (!maps.ContainsKey(parentPath) || Array.FindIndex(sequence, p => Path.GetFullPath(p) == parentPath) >= Array.FindIndex(sequence, p => Path.GetFullPath(p) == entry)))
                    throw new InvalidOperationException("父阶段必须是同组中更早的地图：" + parentFile);
                affected = string.Equals(entry, path, StringComparison.OrdinalIgnoreCase) || parentPath != null && output.ContainsKey(parentPath);
                if (!affected) continue;
                var result = parentPath == null ? maps[entry] : Resolve(maps[parentPath], maps[entry]);
                try { ValidateBounds(result); }
                catch (Exception ex) { throw new InvalidOperationException(Path.GetFileName(entry) + " 继承冲突，全部地图均未保存：" + ex.Message); }
                var report = LevelValidator.ValidateStageSave(result);
                if (report.HasStructureErrors) throw new InvalidOperationException(Path.GetFileName(entry) + " 继承冲突，全部地图均未保存：" + report);
                maps[entry] = result;
                output[entry] = result;
            }
            if (!output.ContainsKey(path)) output[path] = draft;
            return output;
        }

        public static void WriteBatch(Dictionary<string, LevelDefinition> maps)
        {
            var original = maps.Keys.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
            var written = new List<string>();
            try
            {
                foreach (var pair in maps)
                {
                    string content = LevelJsonSerializer.ToJson(pair.Value).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
                    string temp = pair.Key + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temp, content, new UTF8Encoding(false));
                        if (File.Exists(pair.Key)) File.Replace(temp, pair.Key, null); else File.Move(temp, pair.Key);
                        written.Add(pair.Key);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
            catch
            {
                foreach (var path in written)
                    if (original[path] == null) File.Delete(path); else File.WriteAllBytes(path, original[path]);
                throw;
            }
        }
    }
}
