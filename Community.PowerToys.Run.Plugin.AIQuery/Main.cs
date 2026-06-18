using System.IO;
using System.Windows;
using System.Windows.Controls;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.AIQuery;

public class Main : IPlugin, ISettingProvider, IContextMenu, IDisposable
{
    public static string PluginID => "B3F4A2C1D8E5497F9A0B1C2D3E4F5A6B";
    public string Name => "AI Query";
    public string Description => "Ask AI a question directly from PowerToys Run";

    private PluginInitContext? _context;
    private AIClient? _client;
    private HistoryManager? _history;
    private readonly PluginSettings _settings = new();

    // State machine
    private readonly object _stateLock = new();
    private string _currentQuestion = "";
    private string? _pendingAnswer = null;
    private string? _pendingError = null;
    private bool _isLoading = false;
    private CancellationTokenSource? _cts;
    private string _lastAnswer = string.Empty;

    public void Init(PluginInitContext context)
    {
        _context = context;
        _history = new HistoryManager(context.CurrentPluginMetadata.PluginDirectory);
        RebuildClient();
    }

    public List<Result> Query(Query query)
    {
        var search = query.Search.Trim();

        // Empty input → show history
        if (string.IsNullOrEmpty(search))
            return HistoryResults(null, query.RawQuery);

        // "h " prefix → search history
        if (search.StartsWith("h ", StringComparison.OrdinalIgnoreCase) ||
            search.Equals("h", StringComparison.OrdinalIgnoreCase))
        {
            var keyword = search.Length > 2 ? search[2..].Trim() : "";
            return HistoryResults(keyword, query.RawQuery);
        }

        bool isFollowUp = search.StartsWith('+');
        var question = isFollowUp ? search[1..].Trim() : search;
        if (string.IsNullOrEmpty(question))
            return [HintResult()];

        lock (_stateLock)
        {
            if (question != _currentQuestion && _isLoading)
            {
                _cts?.Cancel();
                _isLoading = false;
                _pendingAnswer = null;
                _pendingError = null;
            }

            if (question == _currentQuestion)
            {
                if (_pendingError != null)
                    return [ErrorResult(_pendingError)];
                if (_pendingAnswer != null)
                    return [AnswerResult(_pendingAnswer)];
                if (_isLoading)
                    return [ThinkingResult(question)];
            }
        }

        return [ReadyResult(question, query.RawQuery, isFollowUp)];
    }

    // ── History display ────────────────────────────────────────────────────────

    private List<Result> HistoryResults(string? keyword, string rawQuery)
    {
        if (_history == null) return [HintResult()];

        var entries = keyword is null or ""
            ? _history.Entries.Take(15).ToList()
            : _history.Search(keyword).Take(15).ToList();

        if (entries.Count == 0)
        {
            return
            [
                new Result
                {
                    Title = keyword is null or "" ? "暂无历史记录" : $"未找到「{keyword}」相关记录",
                    SubTitle = "提问后会自动保存到历史",
                    IcoPath = GetIconPath(),
                    Score = 0,
                    Action = _ => false,
                },
            ];
        }

        return entries.Select((entry, i) =>
        {
            var ago = FormatTimeAgo(entry.Timestamp);
            var answerPreview = entry.Answer.Length > 80
                ? entry.Answer[..80].TrimEnd() + "…"
                : entry.Answer;

            return new Result
            {
                Title = entry.Question.Length > 60
                    ? entry.Question[..60].TrimEnd() + "…"
                    : entry.Question,
                SubTitle = $"{answerPreview}\n  {entry.Model}  ·  {ago}  ·  Enter 复制  /  Ctrl+Enter 重新提问",
                IcoPath = GetIconPath(),
                Score = 100 - i,
                Action = _ =>
                {
                    Clipboard.SetText(entry.Answer);
                    return true;
                },
                ContextData = entry,
            };
        }).ToList();
    }

    // ── Ask / state machine ───────────────────────────────────────────────────

    private Result ReadyResult(string question, string rawQuery, bool isFollowUp) => new()
    {
        Title = "⏎  按 Enter 发送",
        SubTitle = $"「{(question.Length > 50 ? question[..50] + "…" : question)}」",
        IcoPath = GetIconPath(),
        Score = 100,
        Action = _ =>
        {
            lock (_stateLock)
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                _currentQuestion = question;
                _isLoading = true;
                _pendingAnswer = null;
                _pendingError = null;
            }

            _context?.API.ChangeQuery(rawQuery, true);

            var ct = _cts!.Token;
            string? context = isFollowUp && !string.IsNullOrEmpty(_lastAnswer) ? _lastAnswer : null;

            Task.Run(async () =>
            {
                try
                {
                    var answer = await _client!.QueryAsync(question, context, ct);
                    lock (_stateLock)
                    {
                        if (_currentQuestion == question)
                        {
                            _pendingAnswer = answer;
                            _lastAnswer = answer;
                            _isLoading = false;
                        }
                    }
                    _history?.Add(question, answer, _settings.Model);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    lock (_stateLock)
                    {
                        if (_currentQuestion == question)
                        {
                            _pendingError = ex.Message;
                            _isLoading = false;
                        }
                    }
                }
                finally
                {
                    _context?.API.ChangeQuery(rawQuery, true);
                }
            }, ct);

            return false;
        },
    };

    private Result AnswerResult(string answer)
    {
        var modelLabel = _settings.Model.Length > 24 ? _settings.Model[..24] : _settings.Model;
        var subText = $"{answer}\n─────────────────────────────\n  {modelLabel}  ·  Enter 复制  /  Ctrl+Enter 记事本打开";

        return new Result
        {
            Title = "💬 Answer",
            SubTitle = subText,
            IcoPath = GetIconPath(),
            ToolTipData = new ToolTipData("AI 完整回答", answer),
            Score = 100,
            Action = _ =>
            {
                Clipboard.SetText(answer);
                return true;
            },
            ContextData = answer,
        };
    }

    // ── Context menus ─────────────────────────────────────────────────────────

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        // Context menu for answer result
        if (selectedResult.ContextData is string answer)
        {
            return
            [
                new ContextMenuResult
                {
                    Title = "复制完整回答 (Enter)",
                    Glyph = "\xE8C8",
                    FontFamily = "Segoe MDL2 Assets",
                    AcceleratorKey = System.Windows.Input.Key.Enter,
                    Action = _ => { Clipboard.SetText(answer); return true; },
                },
                new ContextMenuResult
                {
                    Title = "在记事本中打开 (Ctrl+Enter)",
                    Glyph = "\xE70F",
                    FontFamily = "Segoe MDL2 Assets",
                    AcceleratorKey = System.Windows.Input.Key.Enter,
                    AcceleratorModifiers = System.Windows.Input.ModifierKeys.Control,
                    Action = _ =>
                    {
                        var tmp = Path.Combine(Path.GetTempPath(), $"ai-answer-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                        File.WriteAllText(tmp, answer);
                        System.Diagnostics.Process.Start("notepad.exe", tmp);
                        return true;
                    },
                },
            ];
        }

        // Context menu for history entries
        if (selectedResult.ContextData is HistoryEntry entry)
        {
            return
            [
                new ContextMenuResult
                {
                    Title = "复制回答 (Enter)",
                    Glyph = "\xE8C8",
                    FontFamily = "Segoe MDL2 Assets",
                    AcceleratorKey = System.Windows.Input.Key.Enter,
                    Action = _ => { Clipboard.SetText(entry.Answer); return true; },
                },
                new ContextMenuResult
                {
                    Title = "重新提问 (Ctrl+Enter)",
                    Glyph = "\xE72C",
                    FontFamily = "Segoe MDL2 Assets",
                    AcceleratorKey = System.Windows.Input.Key.Enter,
                    AcceleratorModifiers = System.Windows.Input.ModifierKeys.Control,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"? {entry.Question}", true);
                        return false;
                    },
                },
                new ContextMenuResult
                {
                    Title = "删除此记录 (Delete)",
                    Glyph = "\xE74D",
                    FontFamily = "Segoe MDL2 Assets",
                    AcceleratorKey = System.Windows.Input.Key.Delete,
                    Action = _ =>
                    {
                        _history?.Remove(entry);
                        _context?.API.ChangeQuery("? ", true);
                        return false;
                    },
                },
            ];
        }

        return [];
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public Control CreateSettingPanel() => throw new NotImplementedException();

    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        if (settings?.AdditionalOptions == null) return;
        foreach (var opt in settings.AdditionalOptions)
        {
            switch (opt.Key)
            {
                case "Provider":
                    _settings.Provider = opt.ComboBoxValue == 0 ? AIProvider.Claude : AIProvider.OpenAICompatible;
                    break;
                case "ApiKey":
                    _settings.ApiKey = opt.TextValue ?? string.Empty;
                    break;
                case "BaseUrl":
                    _settings.BaseUrl = string.IsNullOrWhiteSpace(opt.TextValue)
                        ? "https://api.openai.com/v1" : opt.TextValue;
                    break;
                case "Model":
                    _settings.Model = string.IsNullOrWhiteSpace(opt.TextValue)
                        ? "claude-sonnet-4-6" : opt.TextValue;
                    break;
                case "TimeoutSeconds":
                    if (int.TryParse(opt.TextValue, out var t) && t > 0)
                        _settings.TimeoutSeconds = t;
                    break;
                case "GlobalMode":
                    _settings.GlobalMode = opt.Value;
                    break;
            }
        }
        RebuildClient();
    }

    public IEnumerable<PluginAdditionalOption> AdditionalOptions =>
    [
        new()
        {
            Key = "Provider",
            DisplayLabel = "AI Provider",
            DisplayDescription = "Claude (Anthropic) or OpenAI-compatible API",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Combobox,
            ComboBoxValue = (int)_settings.Provider,
            ComboBoxItems =
            [
                new KeyValuePair<string, string>("Claude (Anthropic)", "0"),
                new KeyValuePair<string, string>("OpenAI-Compatible", "1"),
            ],
        },
        new()
        {
            Key = "ApiKey",
            DisplayLabel = "API Key",
            DisplayDescription = "Your API key",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = _settings.ApiKey,
        },
        new()
        {
            Key = "BaseUrl",
            DisplayLabel = "Base URL",
            DisplayDescription = "OpenAI-compatible base URL (e.g. https://api.openai.com/v1)",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = _settings.BaseUrl,
        },
        new()
        {
            Key = "Model",
            DisplayLabel = "Model",
            DisplayDescription = "Model name (e.g. claude-sonnet-4-6, gpt-4o)",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = _settings.Model,
        },
        new()
        {
            Key = "TimeoutSeconds",
            DisplayLabel = "Timeout (seconds)",
            DisplayDescription = "Request timeout in seconds",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = _settings.TimeoutSeconds.ToString(),
        },
        new()
        {
            Key = "GlobalMode",
            DisplayLabel = "Global Mode",
            DisplayDescription = "Show AI result for all queries (no keyword needed)",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
            Value = _settings.GlobalMode,
        },
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Result HintResult() => new()
    {
        Title = "AI Query",
        SubTitle = "? 问题  ·  ?+ 追问  ·  ? 留空查看历史  ·  ?h 关键词 搜索历史",
        IcoPath = GetIconPath(),
        Score = 0,
        Action = _ => false,
    };

    private Result ThinkingResult(string question) => new()
    {
        Title = "⏳ 正在思考...",
        SubTitle = $"「{(question.Length > 50 ? question[..50] + "…" : question)}」",
        IcoPath = GetIconPath(),
        Score = 100,
        Action = _ => false,
    };

    private Result ErrorResult(string message) => new()
    {
        Title = "❌ 请求失败",
        SubTitle = message,
        IcoPath = GetIconPath(),
        Score = 0,
        Action = _ => false,
    };

    private string GetIconPath()
    {
        var theme = _context?.API.GetCurrentTheme();
        return theme == Theme.Light || theme == Theme.HighContrastWhite
            ? "Images\\ai.light.png"
            : "Images\\ai.dark.png";
    }

    private static string FormatTimeAgo(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} 小时前";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} 天前";
        return dt.ToString("MM-dd");
    }

    private void RebuildClient()
    {
        _client?.Dispose();
        _client = new AIClient(_settings);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
    }
}
