using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Tools;
using MoodleConnector.Infrastructure;
using MoodleConnector.Presentation;
using MoodleConnector.Presentation.Configuration;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Presentation.Tools.Portal;

[McpServerToolType]
public sealed class PortalAgendaTools(ConnectorDbContext dbContext, PortalMcpIdentityResolver identityResolver)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(
        Name = "list_agenda_events",
        Title = "Consultar agenda do portal",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<PortalAgendaListResponse>))]
    [MoodleToolMetadata(
        Family = "portal-agenda",
        Classification = "R1",
        Kind = "read",
        CanonicalOperation = "portal.agenda.list",
        RequiredPlatformPermission = "agenda.manage",
        Evidence = "Consulta eventos da agenda persistidos no portal para o usuário autenticado.")]
    [Description("Lista os eventos da agenda do portal em um intervalo de datas. Não consulta eventos do Moodle.")]
    public async Task<CallToolResult> ListAsync(
        [Description("Início do intervalo em ISO 8601. Padrão: agora.")] DateTimeOffset? from = null,
        [Description("Fim do intervalo em ISO 8601. Padrão: 30 dias após o início.")] DateTimeOffset? to = null,
        [Description("Limite de eventos, entre 1 e 200.")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var identity = await identityResolver.ResolveAsync(cancellationToken);
            var start = from ?? DateTimeOffset.UtcNow;
            var end = to ?? start.AddDays(30);
            if (end <= start)
                throw new ArgumentException("O fim do intervalo deve ser posterior ao início.", nameof(to));

            limit = Math.Clamp(limit, 1, 200);
            var events = await dbContext.CalendarEvents
                .AsNoTracking()
                .Where(item => item.OwnerId == identity.Id && item.StartAt >= start && item.StartAt < end)
                .OrderBy(item => item.StartAt)
                .Take(limit)
                .Select(item => ToDto(item))
                .ToListAsync(cancellationToken);

            return Success(new PortalAgendaListResponse(events, events.Count, start, end),
                $"{events.Count} evento(s) retornado(s).");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultHelper.Error<PortalAgendaListResponse>(exception.Message, errorCode: "portal_agenda_invalid_request");
        }
    }

    [McpServerTool(
        Name = "create_agenda_event",
        Title = "Criar evento na agenda",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<PortalAgendaWriteResponse>))]
    [MoodleToolMetadata(
        Family = "portal-agenda",
        Classification = "R2",
        Kind = "write",
        CanonicalOperation = "portal.agenda.create",
        RequiredPlatformPermission = "agenda.manage",
        Evidence = "Cria um evento privado na agenda do portal.")]
    [Description("Cria um evento na agenda do portal para o usuário autenticado. O evento não é criado no Moodle.")]
    public async Task<CallToolResult> CreateAsync(
        [Description("Título do evento.")] string title,
        [Description("Início do evento em ISO 8601.")] DateTimeOffset startAt,
        [Description("Fim opcional do evento em ISO 8601.")] DateTimeOffset? endAt = null,
        [Description("Descrição opcional.")] string? description = null,
        [Description("Tipo: meeting, alignment, delivery, training, webclass ou other.")] string? type = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var identity = await identityResolver.ResolveAsync(cancellationToken);
            ValidateRange(startAt, endAt);
            var now = DateTimeOffset.UtcNow;
            var calendarEvent = new CalendarEventEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = identity.Id,
                Title = PortalMcpValueNormalizer.RequireTitle(title),
                Description = PortalMcpValueNormalizer.NormalizeDescription(description),
                StartAt = startAt,
                EndAt = endAt,
                Type = PortalMcpValueNormalizer.NormalizeCalendarEventType(type),
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.CalendarEvents.Add(calendarEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Success(new PortalAgendaWriteResponse("created", ToDto(calendarEvent)), "Evento criado na agenda.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultHelper.Error<PortalAgendaWriteResponse>(exception.Message, errorCode: "portal_agenda_invalid_request");
        }
    }

    [McpServerTool(
        Name = "update_agenda_event",
        Title = "Editar evento da agenda",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<PortalAgendaWriteResponse>))]
    [MoodleToolMetadata(
        Family = "portal-agenda",
        Classification = "R2",
        Kind = "write",
        CanonicalOperation = "portal.agenda.update",
        RequiredPlatformPermission = "agenda.manage",
        Evidence = "Atualiza um evento privado da agenda do portal.")]
    [Description("Edita um evento da agenda pertencente ao usuário autenticado. Campos omitidos são preservados.")]
    public async Task<CallToolResult> UpdateAsync(
        [Description("UUID do evento.")] Guid eventId,
        [Description("Novo título, quando necessário.")] string? title = null,
        [Description("Novo início em ISO 8601.")] DateTimeOffset? startAt = null,
        [Description("Novo fim em ISO 8601.")] DateTimeOffset? endAt = null,
        [Description("Nova descrição; envie texto vazio para limpar.")] string? description = null,
        [Description("Novo tipo do evento.")] string? type = null,
        [Description("Limpa o fim do evento quando true.")] bool clearEndAt = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("Informe um eventId UUID válido.", nameof(eventId));

            var identity = await identityResolver.ResolveAsync(cancellationToken);
            var calendarEvent = await dbContext.CalendarEvents
                .SingleOrDefaultAsync(item => item.Id == eventId && item.OwnerId == identity.Id, cancellationToken);
            if (calendarEvent is null)
                return ToolResultHelper.Error<PortalAgendaWriteResponse>("Evento não encontrado.", errorCode: "portal_agenda_event_not_found");

            var nextStart = startAt ?? calendarEvent.StartAt;
            var nextEnd = clearEndAt ? null : endAt ?? calendarEvent.EndAt;
            ValidateRange(nextStart, nextEnd);

            if (title is not null)
                calendarEvent.Title = PortalMcpValueNormalizer.RequireTitle(title);
            if (description is not null)
                calendarEvent.Description = PortalMcpValueNormalizer.NormalizeDescription(description);
            if (startAt is not null)
                calendarEvent.StartAt = startAt.Value;
            if (clearEndAt || endAt is not null)
                calendarEvent.EndAt = nextEnd;
            if (type is not null)
                calendarEvent.Type = PortalMcpValueNormalizer.NormalizeCalendarEventType(type);

            calendarEvent.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Success(new PortalAgendaWriteResponse("updated", ToDto(calendarEvent)), "Evento atualizado.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultHelper.Error<PortalAgendaWriteResponse>(exception.Message, errorCode: "portal_agenda_invalid_request");
        }
    }

    [McpServerTool(
        Name = "remove_agenda_event",
        Title = "Remover evento da agenda",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<PortalAgendaRemovalResponse>))]
    [MoodleToolMetadata(
        Family = "portal-agenda",
        Classification = "R3",
        Kind = "destructive-write",
        CanonicalOperation = "portal.agenda.remove",
        RequiredPlatformPermission = "agenda.manage",
        Evidence = "Remove um evento privado da agenda do portal.")]
    [Description("Remove um evento da agenda pertencente ao usuário autenticado. É uma operação destrutiva e exige confirmação do cliente MCP.")]
    public async Task<CallToolResult> RemoveAsync(
        [Description("UUID do evento.")] Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("Informe um eventId UUID válido.", nameof(eventId));

            var identity = await identityResolver.ResolveAsync(cancellationToken);
            var calendarEvent = await dbContext.CalendarEvents
                .SingleOrDefaultAsync(item => item.Id == eventId && item.OwnerId == identity.Id, cancellationToken);
            if (calendarEvent is null)
                return ToolResultHelper.Error<PortalAgendaRemovalResponse>("Evento não encontrado.", errorCode: "portal_agenda_event_not_found");

            dbContext.CalendarEvents.Remove(calendarEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Success(new PortalAgendaRemovalResponse(eventId, true), "Evento removido da agenda.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultHelper.Error<PortalAgendaRemovalResponse>(exception.Message, errorCode: "portal_agenda_invalid_request");
        }
    }

    private static void ValidateRange(DateTimeOffset startAt, DateTimeOffset? endAt)
    {
        if (endAt is not null && endAt <= startAt)
            throw new ArgumentException("O fim do evento deve ser posterior ao início.", nameof(endAt));
    }

    private static CalendarEventDto ToDto(CalendarEventEntity calendarEvent) =>
        new(calendarEvent.Id, calendarEvent.Title, calendarEvent.Description, calendarEvent.StartAt, calendarEvent.EndAt, calendarEvent.Type, calendarEvent.CreatedAt, calendarEvent.UpdatedAt);

    private static CallToolResult Success<T>(T data, string message)
    {
        var response = new ToolResponse<T>("ok", data, [], Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, Message: message);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response, JsonOptions),
            IsError = false
        };
    }
}

public sealed record PortalAgendaListResponse(
    [property: JsonPropertyName("events")] IReadOnlyList<CalendarEventDto> Events,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("from")] DateTimeOffset From,
    [property: JsonPropertyName("to")] DateTimeOffset To);

public sealed record PortalAgendaWriteResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("event")] CalendarEventDto Event);

public sealed record PortalAgendaRemovalResponse(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("removed")] bool Removed);
