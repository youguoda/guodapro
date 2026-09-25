using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ListSpike;

internal enum CardKind
{
    Text,
    Image,
    Files,
}

/// <summary>
/// One row of the list under test. Content is chosen so any viewport holds
/// cards of visibly different heights; generation is seeded so two runs probe
/// the same deck.
/// </summary>
internal sealed class CardData : INotifyPropertyChanged
{
    public int Index { get; init; }
    public CardKind Kind { get; init; }
    public required string[] TextLines { get; init; }
    public int ImageHeight { get; init; }
    public required string[] Files { get; init; }

    public string Title =>
        $"#{Index:00000} · {Kind switch { CardKind.Text => "文本", CardKind.Image => "图片", _ => "文件" }}";

    public string Body => string.Join("\n", TextLines);

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set { _isPinned = value; Changed(nameof(IsPinned)); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; Changed(nameof(IsSelected)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class Deck
{
    private static readonly string[] Words =
        "剪贴板 历史 翻译 徽标 面板 快速条 条目 置顶 标签 保留策略 缩略图 排除规则 动作 翻译后端 流式 渲染".Split(' ');

    public static ObservableCollection<CardData> Generate(int count)
    {
        var random = new Random(20260925);
        var cards = new List<CardData>(count);

        for (var i = 0; i < count; i++)
        {
            var kind = i % 9 == 4 ? CardKind.Image : i % 13 == 7 ? CardKind.Files : CardKind.Text;

            cards.Add(new CardData
            {
                Index = i,
                Kind = kind,
                TextLines = TextLines(random, i),
                ImageHeight = 60 + (35 * random.Next(0, 5)),
                Files = kind == CardKind.Files
                    ? Enumerable.Range(0, 1 + random.Next(0, 4)).Select(k => $"文档-{i:00000}-{k}.pdf").ToArray()
                    : Array.Empty<string>(),
            });
        }

        return new ObservableCollection<CardData>(cards);
    }

    private static string[] TextLines(Random random, int index)
    {
        var lineCount = 1 + (index % 6);

        return Enumerable.Range(0, lineCount)
            .Select(_ => string.Join(' ', Enumerable.Range(0, 4 + random.Next(0, 11))
                .Select(_ => Words[random.Next(Words.Length)])))
            .ToArray();
    }
}
