using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        sealed class InheritanceSummary
        {
            public string Parent;
            public int Version;
        }

        // 继承字段只来自作者源 JSON。面板每次 Layout/Repaint 都读取，不能为标签重建整张地图。
        // 源字符串不可变；保存、重载和撤销换回另一份源时自动命中对应摘要。
        // 弱键不延长旧草稿及其大型继承历史的生命周期，克隆草稿也可共用缓存。
        static readonly ConditionalWeakTable<string, InheritanceSummary> Summaries = new ConditionalWeakTable<string, InheritanceSummary>();
        static readonly InheritanceSummary NoInheritance = new InheritanceSummary();

        static InheritanceSummary Summary(LevelDefinition map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return string.IsNullOrEmpty(map.authoringSourceJson) ? NoInheritance
                : Summaries.GetValue(map.authoringSourceJson, ReadSummary);
        }

        static InheritanceSummary ReadSummary(string source)
        {
            var metadata = JObject.Parse(source)[Metadata];
            return new InheritanceSummary
            {
                Parent = (string)metadata?["parent"],
                Version = (int?)metadata?["version"] ?? 0
            };
        }

        public static string Parent(LevelDefinition map) => Summary(map).Parent;
        public static int Version(LevelDefinition map) => Summary(map).Version;

        // 关于指定右上角的反射是可逆变换；区域两端同时交换，Y 不变。
        static JObject Reframe(JObject source, int anchorX, int anchorZ)
        {
            var result = (JObject)source.DeepClone();
            void Cell(JToken cell)
            {
                cell["x"] = checked(anchorX - (int)cell["x"]);
                cell["z"] = checked(anchorZ - (int)cell["z"]);
            }
            var bounds = result["bounds"];
            int minX = (int)bounds["min"]["x"], minZ = (int)bounds["min"]["z"];
            bounds["min"]["x"] = checked(anchorX - (int)bounds["max"]["x"]);
            bounds["min"]["z"] = checked(anchorZ - (int)bounds["max"]["z"]);
            bounds["max"]["x"] = checked(anchorX - minX);
            bounds["max"]["z"] = checked(anchorZ - minZ);
            foreach (var entity in ((JObject)result["entities"]).Properties()) Cell(entity.Value["cell"]);
            var terrain = new JObject();
            foreach (var cell in ((JObject)result["terrain"]).Properties())
            {
                var c = cell.Name.Split(',').Select(int.Parse).ToArray();
                terrain[checked(anchorX - c[0]) + "," + c[1] + "," + checked(anchorZ - c[2])] = cell.Value.DeepClone();
            }
            result["terrain"] = terrain;
            return result;
        }

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
        static JObject Layout(JObject map, bool upperRight = false)
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
            return upperRight ? Reframe(result, (int)map["bounds"]["max"]["x"], (int)map["bounds"]["max"]["z"]) : result;
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
            // 保留子阶段的实体顺序，避免无布局变化的保存改变运行时遍历顺序。
            var entities = (JObject)layout["entities"];
            var order = ((JArray)map["entities"]).Select(e => (string)e["id"])
                .Concat(entities.Properties().Select(p => p.Name)).Distinct();
            map["entities"] = new JArray(order.Where(id => entities[id] != null).Select(id => entities[id].DeepClone()));
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
            var layout = Layout(json, true);
            var changes = new JArray();
            Diff(Layout(Json(parent), true), layout, new List<string>(), changes);
            json[Metadata] = new JObject { ["version"] = 2, ["coordinateSystem"] = "UpperRightXZ_YUnchanged", ["parent"] = parentFile, ["overrides"] = changes, ["resolved"] = layout };
            return LevelJsonSerializer.FromDraftJson(json.ToString());
        }

        // 冲突只撤回本轮继承的位置/地块；保留已有子阶段内容和其他字段。
        static void SkipConflicts(JObject local, JObject result, List<string> warnings)
        {
            var entities = (JObject)result["entities"];
            var terrain = (JObject)result["terrain"];
            string Key(JToken cell) => cell["x"] + "," + cell["y"] + "," + cell["z"];
            bool Inside(JToken cell)
            {
                return new[] { "x", "y", "z" }.All(a => (int)cell[a] >= (int)result["bounds"]["min"][a]
                    && (int)cell[a] <= (int)result["bounds"]["max"][a]);
            }
            JToken Cell(string key)
            {
                var c = key.Split(',').Select(int.Parse).ToArray();
                return new JObject { ["x"] = c[0], ["y"] = c[1], ["z"] = c[2] };
            }
            bool RestoreEntity(string id)
            {
                var old = local["entities"][id];
                if (old == null) { entities.Remove(id); warnings?.Add("实体 " + id + "：跳过新增（占格或越界）"); return true; }
                if (JToken.DeepEquals(entities[id]["cell"], old["cell"])) return false;
                entities[id]["cell"] = old["cell"].DeepClone();
                warnings?.Add("实体 " + id + "：保留原位置（占格或越界）");
                return true;
            }
            bool RestoreTerrain(string key)
            {
                if (JToken.DeepEquals(terrain[key], local["terrain"][key])) return false;
                if (local["terrain"][key] == null) terrain.Remove(key);
                else terrain[key] = local["terrain"][key].DeepClone();
                warnings?.Add("地块 " + key + "：跳过继承（占格或越界）");
                return true;
            }
            // 每轮至少撤回一个继承位置、地块或尺寸，直到冲突不再变化。
            bool changed;
            do
            {
                changed = false;
                var bounds = result["bounds"].ToObject<GridCellBox>();
                bool invalidBounds = !MapResize.TryValidateBounds(bounds, out _);
                bool clipsLocal = entities.Properties().Any(e => !Inside(e.Value["cell"])
                    && JToken.DeepEquals(e.Value["cell"], local["entities"][e.Name]?["cell"]))
                    || terrain.Properties().Any(c => !Inside(Cell(c.Name)) && local["terrain"][c.Name] != null);
                if ((invalidBounds || clipsLocal) && !JToken.DeepEquals(result["bounds"], local["bounds"]))
                {
                    result["bounds"] = local["bounds"].DeepClone();
                    warnings?.Add("地图尺寸：保留原边界（尺寸无效或会裁掉子阶段内容）");
                    changed = true;
                }
                foreach (var e in entities.Properties().ToArray())
                    if (!Inside(e.Value["cell"])) changed |= RestoreEntity(e.Name);
                foreach (var c in terrain.Properties().ToArray())
                    if (!Inside(Cell(c.Name))) changed |= RestoreTerrain(c.Name);
                foreach (var group in entities.Properties().GroupBy(e => Key(e.Value["cell"])).Where(g => g.Count() > 1).ToArray())
                    foreach (var e in group.ToArray()) changed |= RestoreEntity(e.Name);
                foreach (var e in entities.Properties().ToArray())
                {
                    string key = Key(e.Value["cell"]);
                    if (terrain[key] == null) continue;
                    // 原有物件优先于新地形；原有地形优先于继承来的移动。
                    if (!RestoreTerrain(key)) changed |= RestoreEntity(e.Name);
                    else changed = true;
                }
            } while (changed);
        }

        public static LevelDefinition Resolve(LevelDefinition parent, LevelDefinition child, List<string> warnings = null)
        {
            ValidateBounds(parent); ValidateBounds(child);
            var json = Json(child);
            var meta = json[Metadata] as JObject ?? throw new InvalidOperationException("尚未建立阶段继承");
            int version = (int?)meta["version"] ?? 0;
            if (version != 1 && version != 2) throw new InvalidOperationException("不支持的阶段继承版本");
            bool upperRight = version == 2;
            var local = Layout(json, upperRight);
            var changes = (JArray)meta["overrides"];
            // 只追加本轮真正编辑的字段；覆盖即使暂时等于父阶段也不会丢失。
            Diff(meta["resolved"], local, new List<string>(), changes);
            var resolved = Apply(Layout(Json(parent), upperRight), changes);
            // 父阶段删除了子阶段改过的实体时，保留完整的子阶段实体，避免残缺字段。
            var entities = (JObject)resolved["entities"];
            foreach (var property in entities.Properties().ToArray())
                if (property.Value["id"] == null && local["entities"][property.Name] != null)
                    entities[property.Name] = local["entities"][property.Name].DeepClone();
            SkipConflicts(local, resolved, warnings);
            var stored = upperRight ? Reframe(resolved, (int)json["bounds"]["max"]["x"], (int)json["bounds"]["max"]["z"]) : resolved;
            Materialize(json, stored);
            meta["resolved"] = resolved;
            if (!JToken.DeepEquals(local, resolved))
                foreach (var solution in (JArray)json["referenceSolutions"]) solution["status"] = "布局继承后待重新验证";
            return LevelJsonSerializer.FromDraftJson(json.ToString());
        }

        public static Dictionary<string, LevelDefinition> BuildSave(string path, LevelDefinition draft, string[] sequence,
            List<string> warnings = null, IEnumerable<string> protectedPaths = null)
        {
            path = Path.GetFullPath(path);
            var paths = sequence.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var blocked = new HashSet<string>((protectedPaths ?? Array.Empty<string>()).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            var maps = new Dictionary<string, LevelDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in paths)
            {
                try { maps[entry] = LevelJsonSerializer.FromDraftJson(File.ReadAllText(entry, Encoding.UTF8)); }
                catch (Exception ex) { warnings?.Add(Path.GetFileName(entry) + "：跳过继承，无法读取：" + ex.Message); }
            }
            // 建立或升级关系前留下的撤销记录，不应静默解除继承或降级坐标版本。
            if (maps.TryGetValue(path, out var disk) && Version(draft) < Version(disk) && !string.IsNullOrEmpty(Parent(disk)))
            {
                var json = Json(draft);
                json[Metadata] = Json(disk)[Metadata].DeepClone();
                draft = LevelJsonSerializer.FromDraftJson(json.ToString());
            }
            maps[path] = draft;
            var output = new Dictionary<string, LevelDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in paths)
            {
                if (!maps.ContainsKey(entry)) continue;
                bool current = string.Equals(entry, path, StringComparison.OrdinalIgnoreCase);
                string parentFile = Parent(maps[entry]);
                string parentPath = string.IsNullOrEmpty(parentFile) ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry), parentFile));
                if (!current && (parentPath == null || !output.ContainsKey(parentPath))) continue;
                if (!current && blocked.Contains(entry))
                { warnings?.Add(Path.GetFileName(entry) + "：跳过继承，窗口有未保存编辑或正在试玩"); continue; }
                var result = maps[entry];
                if (parentPath != null)
                {
                    try
                    {
                        if (!maps.ContainsKey(parentPath) || Array.FindIndex(paths, p => string.Equals(p, parentPath, StringComparison.OrdinalIgnoreCase)) >= Array.IndexOf(paths, entry))
                            throw new InvalidOperationException("父阶段不在同组较早位置：" + parentFile);
                        var skipped = new List<string>();
                        var candidate = Resolve(maps[parentPath], result, skipped);
                        ValidateBounds(candidate);
                        var report = LevelValidator.ValidateStageSave(candidate);
                        if (report.HasStructureErrors) throw new InvalidOperationException(report.ToString());
                        result = candidate;
                        foreach (string item in skipped) warnings?.Add(Path.GetFileName(entry) + "：" + item);
                    }
                    catch (Exception ex)
                    {
                        warnings?.Add(Path.GetFileName(entry) + "：本阶段跳过继承，保留原状：" + ex.Message);
                        if (!current) continue;
                    }
                }
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
