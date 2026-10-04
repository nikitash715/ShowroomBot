using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace ShowroomBot.Configuration;

/// <summary>Patch value spans rather than re-emitting the document, retaining comments and unknown keys.</summary>
public static class CommentedYaml
{
    public static string Update(string original, string desired)
    {
        YamlNode Read(string text)
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(text));
            if (yaml.Documents.Count != 1) throw new InvalidDataException("Ожидается один YAML-документ.");
            return yaml.Documents[0].RootNode;
        }
        var edits = new List<(int Start, int Length, string Text)>();
        void Merge(YamlNode old, YamlNode next)
        {
            if (old is YamlMappingNode map && next is YamlMappingNode wanted)
            {
                var missing = new YamlMappingNode();
                foreach (var entry in wanted.Children)
                    if (map.Children.TryGetValue(entry.Key, out var value)) Merge(value, entry.Value);
                    else missing.Add(entry.Key, entry.Value);
                if (missing.Children.Count > 0)
                {
                    var index = (int)map.End.Index;
                    while (index > 0 && original[index - 1] is ' ' or '\t') index--;
                    var indent = map.Children.Count == 0 ? 0 : (int)map.Children.First().Key.Start.Column - 1;
                    var rendered = Render(missing).TrimEnd('\r', '\n');
                    var text = string.Join("\n", rendered.Split('\n').Select(l => new string(' ', indent) + l.TrimEnd('\r'))) + "\n";
                    if (index > 0 && original[index - 1] != '\n') text = "\n" + text;
                    edits.Add((index, 0, text));
                }
            }
            else if (old is YamlSequenceNode seq && next is YamlSequenceNode desiredSeq && seq.Children.Count == desiredSeq.Children.Count)
            {
                for (var i = 0; i < seq.Children.Count; i++) Merge(seq.Children[i], desiredSeq.Children[i]);
            }
            else if (!old.Equals(next))
            {
                var start = (int)old.Start.Index;
                var end = (int)old.End.Index;
                if (old is YamlSequenceNode oldSequence && oldSequence.Children.Count > 0)
                {
                    end = oldSequence.Children.Max(n => (int)n.End.Index);
                    if (original[start] != '[')
                    {
                        var lineEnd = original.IndexOf('\n', end);
                        end = lineEnd < 0 ? original.Length : lineEnd + 1;
                    }
                }
                var replacement = Render(next).TrimEnd('\r', '\n');
                if (old is YamlSequenceNode)
                {
                    // Keep comments from the replaced collection at their original indentation.
                    var comments = new List<string>();
                    var parser = new Parser(new Scanner(new StringReader(original), false));
                    while (parser.MoveNext())
                        if (parser.Current is Comment comment && comment.Start.Index >= start && comment.Start.Index < end)
                            comments.Add(new string(' ', (int)old.Start.Column - 1) + "# " + comment.Value);
                    var flow = new YamlSequenceNode(((YamlSequenceNode)next).Children) { Style = SequenceStyle.Flow };
                    replacement = string.Join("\n", comments.Append(new string(' ', (int)old.Start.Column - 1) +
                        Render(flow).Trim())) + "\n";
                    replacement = replacement.TrimStart(' ');
                }
                edits.Add((start, end - start, replacement));
            }
        }
        Merge(Read(original), Read(desired));
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            original = original.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        Read(original); // Never return malformed YAML for writing.
        return original;
    }

    private static string Render(YamlNode node)
    {
        var writer = new StringWriter();
        new YamlStream(new YamlDocument(node)).Save(writer, false);
        return writer.ToString().Replace("...\r\n", "").Replace("...\n", "");
    }
}
