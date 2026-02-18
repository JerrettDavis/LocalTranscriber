using System.Text;

namespace LocalTranscriber.Tests.E2E.Reporting;

public static class HtmlReportGenerator
{
    public static async Task GenerateAsync(string outputPath, TestRunMetadata? metadata = null)
    {
        var dir = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(dir);

        var (total, passed, failed) = TestReportCollector.GetSummary();
        var featureGroups = TestReportCollector.GetResultsByFeature();
        var allTags = TestReportCollector.GetAllTags();
        var passRate = total > 0 ? (int)Math.Round(100.0 * passed / total) : 0;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>E2E Test Report</title>");
        AppendStyles(sb);
        sb.AppendLine("</head><body>");

        // Dashboard
        AppendDashboard(sb, total, passed, failed, passRate, metadata);

        // Filter bar
        AppendFilterBar(sb, allTags);

        // Features
        sb.AppendLine("<div id=\"features-container\">");
        foreach (var feature in featureGroups)
        {
            var featurePassed = feature.Count(s => !s.HasError);
            var featureFailed = feature.Count(s => s.HasError);
            var hasFailures = featureFailed > 0;

            sb.AppendLine($"<div class=\"feature-section{(hasFailures ? " open" : "")}\">");
            sb.AppendLine($"<div class=\"feature-header\" onclick=\"toggleFeature(this)\">");
            sb.AppendLine("<span class=\"chevron\">&#9654;</span>");
            sb.AppendLine($"<span class=\"feature-title\">{Encode(feature.Key)}</span>");
            sb.AppendLine($"<span class=\"badge badge-pass\">{featurePassed} passed</span>");
            if (featureFailed > 0)
                sb.AppendLine($"<span class=\"badge badge-fail\">{featureFailed} failed</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"feature-body\">");

            foreach (var scenario in feature.OrderBy(s => s.Title))
            {
                var isFailed = scenario.HasError;
                var statusClass = isFailed ? "status-fail" : "status-pass";
                var tagsAttr = scenario.Tags.Length > 0
                    ? Encode(string.Join(",", scenario.Tags))
                    : "";
                var durationStr = FormatDuration(scenario.Duration);

                sb.AppendLine($"<div class=\"scenario-card{(isFailed && hasFailures ? " open" : "")}\" data-status=\"{(isFailed ? "failed" : "passed")}\" data-tags=\"{tagsAttr}\" data-title=\"{Encode(scenario.Title.ToLowerInvariant())}\">");
                sb.AppendLine($"<div class=\"scenario-header\" onclick=\"toggleScenario(this)\">");
                sb.AppendLine("<span class=\"chevron\">&#9654;</span>");
                sb.AppendLine($"<span class=\"status-dot {statusClass}\"></span>");
                sb.AppendLine($"<span class=\"scenario-title\">{Encode(scenario.Title)}</span>");
                sb.AppendLine($"<span class=\"scenario-duration\">{durationStr}</span>");
                if (scenario.Tags.Length > 0)
                {
                    foreach (var tag in scenario.Tags)
                        sb.AppendLine($"<span class=\"tag\">@{Encode(tag)}</span>");
                }
                sb.AppendLine("</div>");
                sb.AppendLine("<div class=\"scenario-body\">");

                // Steps
                foreach (var step in scenario.Steps)
                {
                    var stepStatusClass = step.Status switch
                    {
                        StepStatus.Passed => "badge-pass",
                        StepStatus.Failed => "badge-fail",
                        _ => "badge-skip"
                    };
                    var stepDurationStr = FormatDuration(step.Duration);

                    sb.AppendLine("<div class=\"step-row\">");
                    sb.AppendLine($"<span class=\"step-keyword\">{Encode(step.Keyword)}</span>");
                    sb.AppendLine($"<span class=\"step-text\">{Encode(step.Text)}</span>");
                    sb.AppendLine($"<span class=\"badge {stepStatusClass}\">{step.Status}</span>");
                    sb.AppendLine($"<span class=\"step-duration\">{stepDurationStr}</span>");
                    sb.AppendLine("</div>");

                    // Error block
                    if (step.Status == StepStatus.Failed && step.Error is not null)
                    {
                        sb.AppendLine("<div class=\"step-error\">");
                        sb.AppendLine($"<div class=\"error-message\">{Encode(step.Error)}</div>");
                        if (step.StackTrace is not null)
                        {
                            sb.AppendLine("<details class=\"stack-trace\">");
                            sb.AppendLine("<summary>Stack Trace</summary>");
                            sb.AppendLine($"<pre>{Encode(step.StackTrace)}</pre>");
                            sb.AppendLine("</details>");
                        }
                        sb.AppendLine("</div>");
                    }

                    // Screenshots
                    if (step.Screenshots is { Count: > 0 })
                    {
                        sb.AppendLine("<div class=\"step-screenshots\">");
                        sb.AppendLine("<div class=\"profile-tabs\">");
                        for (var pi = 0; pi < step.Screenshots.Count; pi++)
                        {
                            var activeClass = pi == 0 ? " active" : "";
                            sb.AppendLine($"<button class=\"profile-tab{activeClass}\" onclick=\"showProfile(this,{pi})\">{Encode(step.Screenshots[pi].ProfileName)}</button>");
                        }
                        sb.AppendLine("</div>");
                        for (var pi = 0; pi < step.Screenshots.Count; pi++)
                        {
                            var b64 = Convert.ToBase64String(step.Screenshots[pi].Data);
                            var hiddenStyle = pi == 0 ? "" : " style=\"display:none\"";
                            sb.AppendLine($"<img class=\"thumb profile-img\" data-profile-idx=\"{pi}\"{hiddenStyle} src=\"data:image/jpeg;base64,{b64}\" onclick=\"expandImg(this)\" alt=\"{Encode(step.Screenshots[pi].ProfileName)}\">");
                        }
                        sb.AppendLine("</div>");
                    }
                }

                sb.AppendLine("</div>"); // scenario-body
                sb.AppendLine("</div>"); // scenario-card
            }

            sb.AppendLine("</div>"); // feature-body
            sb.AppendLine("</div>"); // feature-section
        }
        sb.AppendLine("</div>"); // features-container

        // Overlay
        sb.AppendLine("<div id=\"overlay\" onclick=\"this.style.display='none'\"><img id=\"overlay-img\" src=\"\"></div>");

        AppendScript(sb);
        sb.AppendLine("</body></html>");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private static string Encode(string text) =>
        System.Net.WebUtility.HtmlEncode(text);

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes}m {ts.Seconds:D2}s";
        if (ts.TotalSeconds >= 1)
            return $"{ts.TotalSeconds:F1}s";
        return $"{ts.TotalMilliseconds:F0}ms";
    }

    private static void AppendDashboard(StringBuilder sb, int total, int passed, int failed, int passRate, TestRunMetadata? metadata)
    {
        sb.AppendLine("<header class=\"dashboard\">");
        sb.AppendLine("<h1>E2E Test Report</h1>");
        sb.AppendLine("<div class=\"stat-cards\">");
        sb.AppendLine($"<div class=\"stat-card\"><div class=\"stat-value\">{total}</div><div class=\"stat-label\">Total</div></div>");
        sb.AppendLine($"<div class=\"stat-card stat-pass\"><div class=\"stat-value\">{passed}</div><div class=\"stat-label\">Passed</div></div>");
        sb.AppendLine($"<div class=\"stat-card stat-fail\"><div class=\"stat-value\">{failed}</div><div class=\"stat-label\">Failed</div></div>");
        sb.AppendLine($"<div class=\"stat-card\"><div class=\"stat-value\">{passRate}%</div><div class=\"stat-label\">Pass Rate</div></div>");
        sb.AppendLine("</div>");

        // Progress bar
        sb.AppendLine("<div class=\"progress-bar\">");
        sb.AppendLine($"<div class=\"progress-fill\" style=\"width:{passRate}%\"></div>");
        sb.AppendLine("</div>");

        // Run metadata
        sb.AppendLine("<div class=\"run-meta\">");
        if (metadata is not null)
        {
            sb.AppendLine($"<span>Duration: {FormatDuration(metadata.Duration)}</span>");
            sb.AppendLine($"<span>{Encode(metadata.Environment)}</span>");
            sb.AppendLine($"<span>{Encode(metadata.MachineName)}</span>");
        }
        sb.AppendLine($"<span>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("</header>");
    }

    private static void AppendFilterBar(StringBuilder sb, IReadOnlyList<string> allTags)
    {
        sb.AppendLine("<div class=\"filter-bar\">");

        // Status buttons
        sb.AppendLine("<div class=\"filter-group\">");
        sb.AppendLine("<button class=\"filter-btn active\" data-filter=\"all\" onclick=\"setStatusFilter(this,'all')\">All</button>");
        sb.AppendLine("<button class=\"filter-btn\" data-filter=\"passed\" onclick=\"setStatusFilter(this,'passed')\">Passed</button>");
        sb.AppendLine("<button class=\"filter-btn\" data-filter=\"failed\" onclick=\"setStatusFilter(this,'failed')\">Failed</button>");
        sb.AppendLine("</div>");

        // Tag chips
        if (allTags.Count > 0)
        {
            sb.AppendLine("<div class=\"filter-group tag-filters\">");
            foreach (var tag in allTags)
                sb.AppendLine($"<button class=\"tag-chip\" data-tag=\"{Encode(tag)}\" onclick=\"toggleTag(this)\">@{Encode(tag)}</button>");
            sb.AppendLine("</div>");
        }

        // Search
        sb.AppendLine("<div class=\"filter-group\">");
        sb.AppendLine("<input type=\"text\" id=\"search-input\" class=\"search-input\" placeholder=\"Search scenarios...\" oninput=\"applyFilters()\">");
        sb.AppendLine("</div>");

        // Expand/Collapse
        sb.AppendLine("<div class=\"filter-group\">");
        sb.AppendLine("<button class=\"filter-btn\" onclick=\"expandAll()\">Expand All</button>");
        sb.AppendLine("<button class=\"filter-btn\" onclick=\"collapseAll()\">Collapse All</button>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");
    }

    private static void AppendStyles(StringBuilder sb)
    {
        sb.AppendLine("<style>");
        sb.AppendLine("""
            :root {
                --bg-primary: #f5f5f5;
                --bg-surface: #ffffff;
                --bg-surface-alt: #fafafa;
                --bg-hover: #f0f0f0;
                --text-primary: #1a1a1a;
                --text-secondary: #555555;
                --text-muted: #888888;
                --border-primary: #e0e0e0;
                --shadow: 0 1px 3px rgba(0,0,0,.1);
                --color-pass: #2e7d32;
                --color-pass-bg: #c8e6c9;
                --color-fail: #c62828;
                --color-fail-bg: #ffcdd2;
                --color-skip: #f57f17;
                --color-skip-bg: #fff9c4;
                --color-accent: #1565c0;
                --tag-bg: #e3f2fd;
                --tag-text: #1565c0;
                --error-bg: #fff3f3;
                --error-text: #c62828;
                --overlay-bg: rgba(0,0,0,.85);
            }
            @media (prefers-color-scheme: dark) {
                :root {
                    --bg-primary: #121212;
                    --bg-surface: #1e1e1e;
                    --bg-surface-alt: #252525;
                    --bg-hover: #2a2a2a;
                    --text-primary: #e0e0e0;
                    --text-secondary: #aaaaaa;
                    --text-muted: #777777;
                    --border-primary: #333333;
                    --shadow: 0 1px 3px rgba(0,0,0,.4);
                    --color-pass: #66bb6a;
                    --color-pass-bg: #1b3a1b;
                    --color-fail: #ef5350;
                    --color-fail-bg: #3a1b1b;
                    --color-skip: #ffca28;
                    --color-skip-bg: #3a351b;
                    --color-accent: #42a5f5;
                    --tag-bg: #1a2a3a;
                    --tag-text: #64b5f6;
                    --error-bg: #2a1515;
                    --error-text: #ef9a9a;
                    --overlay-bg: rgba(0,0,0,.92);
                }
            }
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                background: var(--bg-primary);
                color: var(--text-primary);
                padding: 20px;
                line-height: 1.5;
            }

            /* Dashboard */
            .dashboard {
                background: var(--bg-surface);
                padding: 24px;
                border-radius: 10px;
                margin-bottom: 16px;
                box-shadow: var(--shadow);
            }
            .dashboard h1 { font-size: 1.5rem; margin-bottom: 16px; }
            .stat-cards { display: flex; gap: 12px; margin-bottom: 16px; }
            .stat-card {
                flex: 1;
                background: var(--bg-surface-alt);
                border: 1px solid var(--border-primary);
                border-radius: 8px;
                padding: 16px;
                text-align: center;
            }
            .stat-value { font-size: 2rem; font-weight: 700; }
            .stat-label { font-size: 0.8rem; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.05em; }
            .stat-pass .stat-value { color: var(--color-pass); }
            .stat-fail .stat-value { color: var(--color-fail); }
            .progress-bar {
                height: 8px;
                background: var(--color-fail-bg);
                border-radius: 4px;
                overflow: hidden;
                margin-bottom: 12px;
            }
            .progress-fill {
                height: 100%;
                background: var(--color-pass);
                border-radius: 4px;
                transition: width 0.3s;
            }
            .run-meta {
                display: flex;
                flex-wrap: wrap;
                gap: 16px;
                font-size: 0.8rem;
                color: var(--text-muted);
            }

            /* Filter bar */
            .filter-bar {
                position: sticky;
                top: 0;
                z-index: 100;
                background: var(--bg-surface);
                padding: 12px 16px;
                border-radius: 10px;
                margin-bottom: 16px;
                box-shadow: var(--shadow);
                display: flex;
                flex-wrap: wrap;
                align-items: center;
                gap: 12px;
            }
            .filter-group { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
            .filter-btn {
                background: var(--bg-surface-alt);
                border: 1px solid var(--border-primary);
                color: var(--text-secondary);
                padding: 6px 14px;
                border-radius: 6px;
                font-size: 0.8rem;
                cursor: pointer;
                font-weight: 500;
                transition: all 0.15s;
            }
            .filter-btn:hover { background: var(--bg-hover); }
            .filter-btn.active {
                background: var(--color-accent);
                color: #fff;
                border-color: var(--color-accent);
            }
            .tag-chip {
                background: var(--tag-bg);
                color: var(--tag-text);
                border: 1px solid transparent;
                padding: 4px 10px;
                border-radius: 12px;
                font-size: 0.75rem;
                cursor: pointer;
                font-weight: 500;
                transition: all 0.15s;
            }
            .tag-chip.active {
                background: var(--color-accent);
                color: #fff;
            }
            .search-input {
                background: var(--bg-surface-alt);
                border: 1px solid var(--border-primary);
                color: var(--text-primary);
                padding: 6px 12px;
                border-radius: 6px;
                font-size: 0.85rem;
                width: 200px;
                outline: none;
            }
            .search-input:focus { border-color: var(--color-accent); }

            /* Features */
            .feature-section {
                background: var(--bg-surface);
                margin-bottom: 12px;
                border-radius: 10px;
                box-shadow: var(--shadow);
                overflow: hidden;
            }
            .feature-header {
                padding: 14px 20px;
                display: flex;
                align-items: center;
                gap: 10px;
                cursor: pointer;
                user-select: none;
                transition: background 0.15s;
            }
            .feature-header:hover { background: var(--bg-hover); }
            .feature-title { font-size: 1.1rem; font-weight: 600; flex: 1; }
            .feature-body {
                display: none;
                padding: 0 16px 16px;
            }
            .feature-section.open > .feature-body { display: block; }
            .chevron {
                font-size: 0.7rem;
                color: var(--text-muted);
                transition: transform 0.2s;
                display: inline-block;
            }
            .feature-section.open > .feature-header > .chevron,
            .scenario-card.open > .scenario-header > .chevron {
                transform: rotate(90deg);
            }

            /* Badges */
            .badge {
                display: inline-block;
                padding: 2px 10px;
                border-radius: 12px;
                font-size: 0.75rem;
                font-weight: 600;
                white-space: nowrap;
            }
            .badge-pass { background: var(--color-pass-bg); color: var(--color-pass); }
            .badge-fail { background: var(--color-fail-bg); color: var(--color-fail); }
            .badge-skip { background: var(--color-skip-bg); color: var(--color-skip); }

            /* Tags */
            .tag {
                display: inline-block;
                background: var(--tag-bg);
                color: var(--tag-text);
                padding: 1px 8px;
                border-radius: 10px;
                font-size: 0.7rem;
                font-weight: 500;
            }

            /* Scenario cards */
            .scenario-card {
                border: 1px solid var(--border-primary);
                border-radius: 8px;
                margin-top: 10px;
                overflow: hidden;
            }
            .scenario-card.hidden { display: none; }
            .scenario-header {
                padding: 10px 14px;
                display: flex;
                align-items: center;
                gap: 8px;
                cursor: pointer;
                user-select: none;
                flex-wrap: wrap;
                transition: background 0.15s;
            }
            .scenario-header:hover { background: var(--bg-hover); }
            .scenario-title { font-weight: 500; flex: 1; min-width: 150px; }
            .scenario-duration { font-size: 0.8rem; color: var(--text-muted); }
            .scenario-body { display: none; padding: 8px 14px 14px; }
            .scenario-card.open > .scenario-body { display: block; }
            .status-dot {
                width: 10px;
                height: 10px;
                border-radius: 50%;
                flex-shrink: 0;
            }
            .status-pass { background: var(--color-pass); }
            .status-fail { background: var(--color-fail); }

            /* Steps */
            .step-row {
                display: flex;
                align-items: center;
                gap: 10px;
                padding: 6px 0;
                border-bottom: 1px solid var(--border-primary);
                transition: background 0.1s;
            }
            .step-row:last-child { border-bottom: none; }
            .step-row:hover { background: var(--bg-hover); }
            .step-keyword { font-weight: 700; min-width: 60px; font-size: 0.85rem; }
            .step-text { flex: 1; font-size: 0.9rem; }
            .step-duration { font-size: 0.75rem; color: var(--text-muted); min-width: 50px; text-align: right; }

            /* Error */
            .step-error {
                background: var(--error-bg);
                border-radius: 6px;
                padding: 10px 14px;
                margin: 6px 0 6px 70px;
            }
            .error-message {
                color: var(--error-text);
                font-size: 0.85rem;
                font-family: monospace;
                white-space: pre-wrap;
                word-break: break-word;
            }
            .stack-trace { margin-top: 8px; }
            .stack-trace summary {
                cursor: pointer;
                font-size: 0.8rem;
                color: var(--text-muted);
                font-weight: 500;
            }
            .stack-trace pre {
                margin-top: 6px;
                font-size: 0.75rem;
                color: var(--text-secondary);
                white-space: pre-wrap;
                word-break: break-word;
                max-height: 300px;
                overflow-y: auto;
            }

            /* Screenshots */
            .step-screenshots {
                margin: 6px 0 6px 70px;
            }
            .profile-tabs { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 6px; }
            .profile-tab {
                background: var(--bg-surface-alt);
                border: 1px solid var(--border-primary);
                color: var(--text-secondary);
                padding: 3px 8px;
                border-radius: 4px;
                font-size: 0.7rem;
                cursor: pointer;
                font-weight: 500;
                transition: all 0.15s;
            }
            .profile-tab.active {
                background: var(--color-accent);
                color: #fff;
                border-color: var(--color-accent);
            }
            .thumb {
                width: 320px;
                max-width: 100%;
                cursor: pointer;
                border-radius: 4px;
                border: 1px solid var(--border-primary);
            }

            /* Overlay */
            #overlay {
                display: none;
                position: fixed;
                top: 0; left: 0;
                width: 100%; height: 100%;
                background: var(--overlay-bg);
                z-index: 1000;
                cursor: pointer;
                justify-content: center;
                align-items: center;
            }
            #overlay img {
                max-width: 95vw;
                max-height: 95vh;
                border-radius: 6px;
            }

            /* Responsive */
            @media (max-width: 768px) {
                body { padding: 10px; }
                .stat-cards { flex-direction: column; }
                .filter-bar { flex-direction: column; align-items: stretch; }
                .filter-group { justify-content: center; }
                .search-input { width: 100%; }
                .step-error, .step-screenshots { margin-left: 0; }
                .step-row { flex-wrap: wrap; }
                .step-keyword { min-width: auto; }
                .scenario-header { gap: 6px; }
            }
            """);
        sb.AppendLine("</style>");
    }

    private static void AppendScript(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine("""
            var activeStatusFilter = 'all';
            var activeTags = new Set();

            function toggleFeature(headerEl) {
                headerEl.parentElement.classList.toggle('open');
            }
            function toggleScenario(headerEl) {
                headerEl.parentElement.classList.toggle('open');
            }

            function expandAll() {
                document.querySelectorAll('.feature-section').forEach(function(f) { f.classList.add('open'); });
                document.querySelectorAll('.scenario-card').forEach(function(s) { s.classList.add('open'); });
            }
            function collapseAll() {
                document.querySelectorAll('.feature-section').forEach(function(f) { f.classList.remove('open'); });
                document.querySelectorAll('.scenario-card').forEach(function(s) { s.classList.remove('open'); });
            }

            function setStatusFilter(btn, status) {
                activeStatusFilter = status;
                document.querySelectorAll('.filter-btn[data-filter]').forEach(function(b) { b.classList.remove('active'); });
                btn.classList.add('active');
                applyFilters();
            }

            function toggleTag(btn) {
                var tag = btn.getAttribute('data-tag');
                if (activeTags.has(tag)) {
                    activeTags.delete(tag);
                    btn.classList.remove('active');
                } else {
                    activeTags.add(tag);
                    btn.classList.add('active');
                }
                applyFilters();
            }

            function applyFilters() {
                var searchText = document.getElementById('search-input').value.toLowerCase();
                var cards = document.querySelectorAll('.scenario-card');

                cards.forEach(function(card) {
                    var status = card.getAttribute('data-status');
                    var tags = card.getAttribute('data-tags');
                    var title = card.getAttribute('data-title');
                    var visible = true;

                    // Status filter
                    if (activeStatusFilter !== 'all' && status !== activeStatusFilter) {
                        visible = false;
                    }

                    // Tag filter (additive — scenario must have ALL selected tags)
                    if (visible && activeTags.size > 0) {
                        var scenarioTags = tags ? tags.split(',') : [];
                        activeTags.forEach(function(t) {
                            if (scenarioTags.indexOf(t) === -1) visible = false;
                        });
                    }

                    // Search filter
                    if (visible && searchText && title.indexOf(searchText) === -1) {
                        visible = false;
                    }

                    card.classList.toggle('hidden', !visible);
                });

                // Hide empty features
                document.querySelectorAll('.feature-section').forEach(function(feature) {
                    var visibleCards = feature.querySelectorAll('.scenario-card:not(.hidden)');
                    feature.style.display = visibleCards.length === 0 ? 'none' : '';
                });
            }

            function showProfile(btn, idx) {
                var container = btn.closest('.step-screenshots');
                container.querySelectorAll('.profile-tab').forEach(function(t) { t.classList.remove('active'); });
                btn.classList.add('active');
                container.querySelectorAll('.profile-img').forEach(function(img) {
                    img.style.display = img.getAttribute('data-profile-idx') == idx ? '' : 'none';
                });
            }

            function expandImg(el) {
                var overlay = document.getElementById('overlay');
                document.getElementById('overlay-img').src = el.src;
                overlay.style.display = 'flex';
            }

            // Escape key dismisses overlay
            document.addEventListener('keydown', function(e) {
                if (e.key === 'Escape') {
                    document.getElementById('overlay').style.display = 'none';
                }
            });

            // Auto-expand features with failures and their failed scenario cards
            (function() {
                document.querySelectorAll('.feature-section').forEach(function(feature) {
                    if (feature.querySelector('.status-fail')) {
                        feature.classList.add('open');
                        feature.querySelectorAll('.scenario-card').forEach(function(card) {
                            if (card.getAttribute('data-status') === 'failed') {
                                card.classList.add('open');
                            }
                        });
                    }
                });
            })();
            """);
        sb.AppendLine("</script>");
    }
}
