import fs from 'node:fs'
/** Applies text replacements to a file and throws if an expected snippet is missing. */
function edit(f, pairs) {
  let s = fs.readFileSync(f, 'utf8')
  for (const [a, b] of pairs) { if (!s.includes(a)) throw new Error('yok ' + f + ': ' + a.slice(0, 70)); s = s.replace(a, b) }
  fs.writeFileSync(f, s)
}
const src = '../src/'

edit(src + 'CrmNlQuery.Application/History/ResultService.cs', [
  ['    public const string DraftKind = "draft";', '    public const string DraftKind = "draft";\n    public const string CreateKind = "create";'],
  ['    // Müşteri özetini sohbete geçmiş kaydı olarak yazar: FetchXML',
`    // Kayıt oluşturma SONUCUNU (yazıldı) sohbete salt okur kart olarak yazar. Yalnız ad, durum, oluşan kayıt kimliği ve hata türü saklanır; alan DEĞERLERİ saklanmaz.
    public async Task<Create.CreateOutcome> PublishCreateAsync(Create.CreateOutcome outcome, Guid owner, string question, string? conversationId = null, CancellationToken ct = default)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            kind = outcome.Kind,
            entity = outcome.Entity,
            entityLabel = outcome.EntityLabel,
            rows = outcome.Rows.Select(r => new { name = r.Name, status = r.Status, recordId = r.RecordId, error = r.Error }).ToList(),
        });
        var fetch = $"<fetch top=\\"1\\"><entity name=\\"{outcome.Entity}\\"><attribute name=\\"{outcome.Entity}id\\" /></entity></fetch>";
        var id = await _history.AddAsync(owner, question, outcome.Entity, fetch, null, conversationId, ct, adminViewable: _adminViewable?.Invoke() ?? false,
            steps: 1, kind: CreateKind, payload: payload);
        return outcome with { ResultId = id, ChatId = conversationId };
    }

    // Kayıtlı oluşturma sonucunu salt okur karta çevirir (CRM'e gidilmez).
    private static Create.CreateOutcome? ReopenCreate(string id, HistoryEntry entry)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(entry.Payload ?? "");
            var root = document.RootElement;
            var rows = new List<Create.CreateRowView>();
            foreach (var r in root.GetProperty("rows").EnumerateArray())
            {
                rows.Add(new Create.CreateRowView(
                    r.GetProperty("name").GetString() ?? "", Array.Empty<Create.CreateFieldView>(), Array.Empty<Create.CreateProblemView>(), Array.Empty<string>(),
                    r.GetProperty("status").GetString() ?? "", r.TryGetProperty("recordId", out var rid) && rid.ValueKind == System.Text.Json.JsonValueKind.String ? rid.GetString() : null,
                    r.TryGetProperty("error", out var err) && err.ValueKind == System.Text.Json.JsonValueKind.String ? err.GetString() : null));
            }
            return new Create.CreateOutcome(root.GetProperty("kind").GetString() ?? "done", root.GetProperty("entity").GetString() ?? "", root.GetProperty("entityLabel").GetString() ?? "", null, rows) { ResultId = id };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // Müşteri özetini sohbete geçmiş kaydı olarak yazar: FetchXML`],
  ['        if (entry.Kind == DraftKind)\n        {', '        if (entry.Kind == CreateKind)\n        {\n            return ReopenCreate(id, entry);\n        }\n        if (entry.Kind == DraftKind)\n        {'],
])

edit(src + 'CrmNlQuery.Application/History/ChatTurnService.cs', [
  ['    // E-posta taslağını sohbete geçmiş kaydı olarak yazar',
`    // Kayıt oluşturma sonucunu (yazıldı) sohbete salt okur kayıt olarak yazar. Durdur kuralı CompleteAsync ile aynıdır.
    public async Task<Create.CreateOutcome> CompleteCreateAsync(Create.CreateOutcome outcome, Guid owner, string question, ChatTurn turn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ct = CancellationToken.None;
        var chat = await _chats.EnsureAsync(owner, turn.ChatId, question, ct);
        return await _results.PublishCreateAsync(outcome, owner, question, chat, ct);
    }

    // E-posta taslağını sohbete geçmiş kaydı olarak yazar`],
])

edit(src + 'CrmNlQuery.Web/Ask/AskEndpoints.cs', [
  ['    private static IResult Refused(HttpContext http, GateDenial denial)', '    internal static IResult Refused(HttpContext http, GateDenial denial)'],
  ['            var callerId = http.Caller();\n\n            var answers = dto.Answers',
`            var callerId = http.Caller();

            // Kayıt oluşturma (human-in-the-loop): kural/bekleyen taslak "evet" derse kapıdan (kota, süre) geçip oluşturma hattına girer; model yalnız değerleri önerir, YAZMA onay uçlarındadır.
            if (services.Create is { } create)
            {
                var wanted = Guid.TryParse(dto.CreateDraftId, out var pendingId) ? pendingId : (Guid?)null;
                if (await create.IsCandidateAsync(dto.Question ?? "", wanted, callerId!, ct))
                {
                    var canCreate = await CrmNlQuery.Web.Security.WidgetPermissions.HasAsync(http, services, CrmNlQuery.Domain.Access.Permissions.RecordsCreate, ct);
                    var attempt = await services.Guard.RunAsync(callerId!, token => create.HandleAsync(dto.Question ?? "", wanted, callerId!, canCreate, token), ct);
                    if (attempt.Denial is { } blocked)
                    {
                        return Refused(http, blocked);
                    }
                    if (attempt.Outcome is { } handled)
                    {
                        if (handled is FailureOutcome { Kind: "timeout" })
                        {
                            return Results.Json(OutcomeDto.From(handled), statusCode: StatusCodes.Status504GatewayTimeout);
                        }
                        return Results.Json(OutcomeDto.From(handled));
                    }
                }
            }

            var answers = dto.Answers`],
])
edit(src + 'CrmNlQuery.Web/Program.cs', [['ChatEndpoints.Map(app, services);', 'ChatEndpoints.Map(app, services);\nCreateEndpoints.Map(app, services);']])
console.log('ok')
