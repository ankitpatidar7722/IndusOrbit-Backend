using System.Text.Json;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// Chat data access (app.ChatRooms + app.ChatMessages, IndusTaskManagement). Rewritten in Dapper
/// from the legacy stored-proc backend. Participants / Reactions / Attachments are JSON columns;
/// membership + unread counts are computed with OPENJSON. Users come from app.Users
/// (FullName→UserName, Role→Designation).
/// </summary>
public sealed class MessagingRepository
{
    private readonly Db _db;
    public MessagingRepository(Db db) => _db = db;

    // Column list shared by every ChatMessage SELECT (adds joined UserName + computed ReplyCount).
    private const string MsgCols = @"
        m.MessageID, m.RoomID, m.CompanyID, m.UserID, m.Content, m.MessageType, m.ParentMessageID,
        m.ReplyDepth, m.CreatedAt, m.EditedAt, m.IsEdited, m.IsDeleted, m.Reactions, m.Attachments,
        ISNULL(m.IsPinned,0) AS IsPinned, m.PinnedBy,
        u.FullName AS UserName,
        ReplyCount = (SELECT COUNT(*) FROM app.ChatMessages cr WHERE cr.ParentMessageID = m.MessageID AND cr.IsDeleted = 0),
        ParentSenderName = (SELECT pu.FullName FROM app.ChatMessages pm JOIN app.Users pu ON pu.UserID = pm.UserID WHERE pm.MessageID = m.ParentMessageID),
        ParentContent    = (SELECT pm.Content     FROM app.ChatMessages pm WHERE pm.MessageID = m.ParentMessageID),
        ParentAttachments= (SELECT pm.Attachments FROM app.ChatMessages pm WHERE pm.MessageID = m.ParentMessageID)";

    // Correlated unread count for a room, relative to the user's lastReadMessageId in Participants.
    private const string UnreadExpr = @"
        (SELECT COUNT(*) FROM app.ChatMessages um
         WHERE um.RoomID = r.RoomID AND um.IsDeleted = 0 AND um.UserID <> @UserID
           AND um.MessageID > ISNULL((SELECT TRY_CONVERT(BIGINT, JSON_VALUE(p.value,'$.lastReadMessageId'))
                                      FROM OPENJSON(ISNULL(r.Participants,'[]')) p
                                      WHERE JSON_VALUE(p.value,'$.userId') = CAST(@UserID AS NVARCHAR(20))), 0))";

    // ── Conversations ─────────────────────────────────────────────────────
    public async Task<IEnumerable<ChatRoomDto>> GetConversationsAsync(int companyId, long userId, string? type)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<ChatRoomDto>($@"
            SELECT r.RoomID, r.CompanyID, r.Type, r.Name, r.Description, r.AvatarUrl, r.IsPublic, r.IsReadOnly,
                   r.CreatedBy, r.CreatedDate, r.UpdatedDate, r.LastMessageAt, r.LastMessagePreview, r.LastMessageBy,
                   r.IsArchived, r.IsDeleted, r.Participants,
                   UnreadCount = {UnreadExpr}
            FROM app.ChatRooms r
            WHERE r.CompanyID = @CompanyID AND r.IsDeleted = 0
              AND (@Type IS NULL OR r.Type = @Type)
              -- participant (active OR left) who hasn't deleted the room for themselves (hiddenAt)
              AND (EXISTS (SELECT 1 FROM OPENJSON(ISNULL(r.Participants,'[]')) p WHERE JSON_VALUE(p.value,'$.userId') = CAST(@UserID AS NVARCHAR(20)) AND JSON_VALUE(p.value,'$.hiddenAt') IS NULL)
                   OR (r.Type = 'Channel' AND r.IsPublic = 1))
            ORDER BY (CASE WHEN r.LastMessageAt IS NULL THEN r.CreatedDate ELSE r.LastMessageAt END) DESC",
            new { CompanyID = companyId, UserID = userId, Type = type });
    }

    public async Task<ChatRoomDto?> GetRoomAsync(int companyId, long userId, long roomId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<ChatRoomDto>($@"
            SELECT r.RoomID, r.CompanyID, r.Type, r.Name, r.Description, r.AvatarUrl, r.IsPublic, r.IsReadOnly,
                   r.CreatedBy, r.CreatedDate, r.UpdatedDate, r.LastMessageAt, r.LastMessagePreview, r.LastMessageBy,
                   r.IsArchived, r.IsDeleted, r.Participants, UnreadCount = {UnreadExpr}
            FROM app.ChatRooms r WHERE r.RoomID = @RoomID AND r.CompanyID = @CompanyID",
            new { CompanyID = companyId, UserID = userId, RoomID = roomId });
    }

    public async Task<ChatRoomDto?> CreateRoomAsync(int companyId, long userId, string? creatorName, CreateRoomRequest req)
    {
        var participants = ParseParticipants(req.ParticipantsJson);
        if (!participants.Any(p => p.userId == userId))
            participants.Insert(0, new ChatParticipant { userId = userId, userName = creatorName ?? "", role = "Owner", isMuted = false, joinedAt = DateTime.UtcNow.ToString("o") });
        var json = JsonSerializer.Serialize(participants);

        await using var c = await _db.OpenAsync();
        var id = await c.ExecuteScalarAsync<long>(@"
            INSERT INTO app.ChatRooms (CompanyID, Type, Name, Description, IsPublic, IsReadOnly, CreatedBy, Participants, CreatedDate, UpdatedDate)
            OUTPUT INSERTED.RoomID
            VALUES (@CompanyID, @Type, @Name, @Description, @IsPublic, @IsReadOnly, @UserID, @Participants, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new { CompanyID = companyId, req.Type, req.Name, req.Description, req.IsPublic, req.IsReadOnly, UserID = userId, Participants = json });
        return await GetRoomAsync(companyId, userId, id);
    }

    public async Task<ChatRoomDto?> GetOrCreateDMAsync(int companyId, long userId, string? userName, long targetUserId, string? targetUserName)
    {
        await using var c = await _db.OpenAsync();
        var existing = await c.QuerySingleOrDefaultAsync<long?>(@"
            SELECT TOP 1 r.RoomID FROM app.ChatRooms r
            WHERE r.CompanyID = @CompanyID AND r.Type = 'DM' AND r.IsDeleted = 0
              AND EXISTS (SELECT 1 FROM OPENJSON(ISNULL(r.Participants,'[]')) p WHERE JSON_VALUE(p.value,'$.userId') = CAST(@U1 AS NVARCHAR(20)))
              AND EXISTS (SELECT 1 FROM OPENJSON(ISNULL(r.Participants,'[]')) p WHERE JSON_VALUE(p.value,'$.userId') = CAST(@U2 AS NVARCHAR(20)))",
            new { CompanyID = companyId, U1 = userId, U2 = targetUserId });
        if (existing.HasValue) return await GetRoomAsync(companyId, userId, existing.Value);

        var participants = new List<ChatParticipant>
        {
            new() { userId = userId, userName = userName ?? "", role = "Member", isMuted = false, joinedAt = DateTime.UtcNow.ToString("o") },
            new() { userId = targetUserId, userName = targetUserName ?? "", role = "Member", isMuted = false, joinedAt = DateTime.UtcNow.ToString("o") },
        };
        var id = await c.ExecuteScalarAsync<long>(@"
            INSERT INTO app.ChatRooms (CompanyID, Type, CreatedBy, Participants, CreatedDate, UpdatedDate)
            OUTPUT INSERTED.RoomID
            VALUES (@CompanyID, 'DM', @UserID, @Participants, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new { CompanyID = companyId, UserID = userId, Participants = JsonSerializer.Serialize(participants) });
        return await GetRoomAsync(companyId, userId, id);
    }

    public async Task<ChatRoomDto?> UpdateRoomAsync(int companyId, long userId, long roomId, UpdateRoomRequest req)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync(@"
            UPDATE app.ChatRooms SET
                Name = COALESCE(@Name, Name),
                Description = COALESCE(@Description, Description),
                IsPublic = COALESCE(@IsPublic, IsPublic),
                IsReadOnly = COALESCE(@IsReadOnly, IsReadOnly),
                Participants = COALESCE(@ParticipantsJson, Participants),
                UpdatedDate = SYSUTCDATETIME()
            WHERE RoomID = @RoomID AND CompanyID = @CompanyID",
            new { req.Name, req.Description, req.IsPublic, req.IsReadOnly, req.ParticipantsJson, RoomID = roomId, CompanyID = companyId });
        return await GetRoomAsync(companyId, userId, roomId);
    }

    public async Task<bool> IsRoomMemberAsync(int companyId, long roomId, long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<int>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM app.ChatRooms r
                WHERE r.RoomID = @RoomID AND r.CompanyID = @CompanyID AND r.IsDeleted = 0
                  AND (EXISTS (SELECT 1 FROM OPENJSON(ISNULL(r.Participants,'[]')) p WHERE JSON_VALUE(p.value,'$.userId') = CAST(@UserID AS NVARCHAR(20)) AND JSON_VALUE(p.value,'$.leftAt') IS NULL)
                       OR (r.Type = 'Channel' AND r.IsPublic = 1))
            ) THEN 1 ELSE 0 END",
            new { RoomID = roomId, CompanyID = companyId, UserID = userId }) == 1;
    }

    // ── Messages ──────────────────────────────────────────────────────────
    public async Task<IEnumerable<ChatMessageDto>> GetMessagesAsync(int companyId, long roomId, long userId, int page, int pageSize)
    {
        await using var c = await _db.OpenAsync();
        // If the viewer has LEFT this group, they only see the history UP TO the moment they left
        // (read-only past conversation — no messages sent after their leave show up).
        var leftAtStr = await c.ExecuteScalarAsync<string?>(@"
            SELECT TOP 1 JSON_VALUE(p.value,'$.leftAt')
            FROM app.ChatRooms r CROSS APPLY OPENJSON(ISNULL(r.Participants,'[]')) p
            WHERE r.RoomID = @RoomID AND r.CompanyID = @CompanyID
              AND JSON_VALUE(p.value,'$.userId') = CAST(@UserID AS NVARCHAR(20))",
            new { RoomID = roomId, CompanyID = companyId, UserID = userId });
        DateTime? leftAt = DateTime.TryParse(leftAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var la) ? la : (DateTime?)null;

        // IsStarred is per-viewing-user (LEFT JOIN stars); "delete for me" rows are excluded via NOT EXISTS.
        var rows = (await c.QueryAsync<ChatMessageDto>($@"
            SELECT {MsgCols},
                   CAST(CASE WHEN st.MessageID IS NULL THEN 0 ELSE 1 END AS bit) AS IsStarred
            FROM app.ChatMessages m
            LEFT JOIN app.Users u ON u.UserId = m.UserID
            LEFT JOIN app.ChatMessageStars st ON st.MessageID = m.MessageID AND st.UserID = @UserID
            WHERE m.CompanyID = @CompanyID AND m.RoomID = @RoomID
              -- replies show INLINE (WhatsApp-style, with a quote of the parent) — not filtered into a separate thread
              AND (@LeftAt IS NULL OR m.CreatedAt <= @LeftAt)
              AND NOT EXISTS (SELECT 1 FROM app.ChatMessageHidden h WHERE h.MessageID = m.MessageID AND h.UserID = @UserID)
            ORDER BY m.CreatedAt DESC
            OFFSET @Off ROWS FETCH NEXT @Take ROWS ONLY",
            new { CompanyID = companyId, RoomID = roomId, UserID = userId, LeftAt = leftAt, Off = (page - 1) * pageSize, Take = pageSize })).ToList();
        rows.Reverse(); // oldest → newest for display
        return rows;
    }

    /// <summary>All messages in a room that shared an attachment or a link — powers the group's
    /// "Media, Docs &amp; Links" gallery. Newest first; excludes deleted + the caller's hidden msgs.</summary>
    public async Task<IEnumerable<ChatMessageDto>> GetSharedMediaAsync(int companyId, long roomId, long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<ChatMessageDto>($@"
            SELECT {MsgCols}
            FROM app.ChatMessages m
            LEFT JOIN app.Users u ON u.UserId = m.UserID
            WHERE m.CompanyID = @CompanyID AND m.RoomID = @RoomID AND m.IsDeleted = 0
              AND ((m.Attachments IS NOT NULL AND m.Attachments <> '' AND m.Attachments <> '[]')
                   OR m.Content LIKE '%http://%' OR m.Content LIKE '%https://%')
              AND NOT EXISTS (SELECT 1 FROM app.ChatMessageHidden h WHERE h.MessageID = m.MessageID AND h.UserID = @UserID)
            ORDER BY m.CreatedAt DESC",
            new { CompanyID = companyId, RoomID = roomId, UserID = userId });
    }

    /// <summary>Pinned (undeleted) messages of a room, oldest → newest, excluding ones the user hid.</summary>
    public async Task<IEnumerable<ChatMessageDto>> GetPinnedAsync(int companyId, long roomId, long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<ChatMessageDto>($@"
            SELECT {MsgCols}
            FROM app.ChatMessages m
            LEFT JOIN app.Users u ON u.UserId = m.UserID
            WHERE m.CompanyID = @CompanyID AND m.RoomID = @RoomID
              AND ISNULL(m.IsPinned,0) = 1 AND m.IsDeleted = 0
              AND NOT EXISTS (SELECT 1 FROM app.ChatMessageHidden h WHERE h.MessageID = m.MessageID AND h.UserID = @UserID)
            ORDER BY m.PinnedAt DESC",
            new { CompanyID = companyId, RoomID = roomId, UserID = userId });
    }

    /// <summary>Pin / unpin a message (visible to the whole room). Returns the message's RoomID for broadcast.</summary>
    public async Task<long?> SetPinAsync(int companyId, long messageId, long userId, bool pinned)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync(@"
            UPDATE app.ChatMessages
            SET IsPinned = @Pinned,
                PinnedBy = CASE WHEN @Pinned = 1 THEN @UserID ELSE NULL END,
                PinnedAt = CASE WHEN @Pinned = 1 THEN SYSUTCDATETIME() ELSE NULL END
            WHERE MessageID = @MessageID AND CompanyID = @CompanyID",
            new { Pinned = pinned, UserID = userId, MessageID = messageId, CompanyID = companyId });
        return await c.ExecuteScalarAsync<long?>("SELECT RoomID FROM app.ChatMessages WHERE MessageID=@MessageID AND CompanyID=@CompanyID",
            new { MessageID = messageId, CompanyID = companyId });
    }

    /// <summary>Star / unstar a message for one user (personal).</summary>
    public async Task SetStarAsync(int companyId, long userId, long messageId, bool starred)
    {
        await using var c = await _db.OpenAsync();
        if (starred)
            await c.ExecuteAsync(@"
                IF NOT EXISTS (SELECT 1 FROM app.ChatMessageStars WHERE UserID=@UserID AND MessageID=@MessageID)
                    INSERT INTO app.ChatMessageStars (UserID, MessageID, CompanyID) VALUES (@UserID, @MessageID, @CompanyID)",
                new { UserID = userId, MessageID = messageId, CompanyID = companyId });
        else
            await c.ExecuteAsync("DELETE FROM app.ChatMessageStars WHERE UserID=@UserID AND MessageID=@MessageID",
                new { UserID = userId, MessageID = messageId });
    }

    /// <summary>Hide a message for one user only ("delete for me").</summary>
    public async Task HideForUserAsync(int companyId, long userId, long messageId)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync(@"
            IF NOT EXISTS (SELECT 1 FROM app.ChatMessageHidden WHERE UserID=@UserID AND MessageID=@MessageID)
                INSERT INTO app.ChatMessageHidden (UserID, MessageID, CompanyID) VALUES (@UserID, @MessageID, @CompanyID)",
            new { UserID = userId, MessageID = messageId, CompanyID = companyId });
    }

    public async Task<ChatMessageDto?> GetMessageAsync(int companyId, long messageId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<ChatMessageDto>($@"
            SELECT {MsgCols} FROM app.ChatMessages m
            LEFT JOIN app.Users u ON u.UserId = m.UserID
            WHERE m.MessageID = @MessageID AND m.CompanyID = @CompanyID",
            new { MessageID = messageId, CompanyID = companyId });
    }

    public async Task<long?> GetMessageRoomIdAsync(int companyId, long messageId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<long?>("SELECT RoomID FROM app.ChatMessages WHERE MessageID=@MessageID AND CompanyID=@CompanyID",
            new { MessageID = messageId, CompanyID = companyId });
    }

    public async Task<ChatMessageDto?> InsertMessageAsync(int companyId, long roomId, long userId, string? content, string? messageType, long? parentMessageId, string? attachmentsJson)
    {
        await using var c = await _db.OpenAsync();
        var depth = 0;
        if (parentMessageId.HasValue)
            depth = (await c.ExecuteScalarAsync<int?>("SELECT ReplyDepth + 1 FROM app.ChatMessages WHERE MessageID=@P", new { P = parentMessageId })) ?? 1;

        var id = await c.ExecuteScalarAsync<long>(@"
            INSERT INTO app.ChatMessages (RoomID, CompanyID, UserID, Content, MessageType, ParentMessageID, ReplyDepth, Attachments, CreatedAt)
            OUTPUT INSERTED.MessageID
            VALUES (@RoomID, @CompanyID, @UserID, @Content, @MessageType, @ParentMessageID, @Depth, @Attachments, SYSUTCDATETIME())",
            new { RoomID = roomId, CompanyID = companyId, UserID = userId, Content = content, MessageType = string.IsNullOrWhiteSpace(messageType) ? "Text" : messageType, ParentMessageID = parentMessageId, Depth = depth, Attachments = attachmentsJson });

        // Update the room's last-message summary for the conversation list.
        var preview = string.IsNullOrWhiteSpace(content) ? "📎 Attachment" : (content!.Length > 200 ? content.Substring(0, 200) : content);
        await c.ExecuteAsync(@"UPDATE app.ChatRooms SET LastMessageAt = SYSUTCDATETIME(), LastMessagePreview = @Preview, LastMessageBy = @UserID, UpdatedDate = SYSUTCDATETIME() WHERE RoomID = @RoomID",
            new { Preview = preview, UserID = userId, RoomID = roomId });

        return await GetMessageAsync(companyId, id);
    }

    public async Task<ChatMessageDto?> EditMessageAsync(int companyId, long messageId, string content)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("UPDATE app.ChatMessages SET Content=@Content, IsEdited=1, EditedAt=SYSUTCDATETIME() WHERE MessageID=@MessageID AND CompanyID=@CompanyID AND IsDeleted=0",
            new { Content = content, MessageID = messageId, CompanyID = companyId });
        return await GetMessageAsync(companyId, messageId);
    }

    public async Task<int> DeleteMessageAsync(int companyId, long messageId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteAsync("UPDATE app.ChatMessages SET IsDeleted=1, Content=NULL, Attachments=NULL WHERE MessageID=@MessageID AND CompanyID=@CompanyID",
            new { MessageID = messageId, CompanyID = companyId });
    }

    public async Task<IEnumerable<ChatMessageDto>> GetRepliesAsync(int companyId, long parentMessageId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<ChatMessageDto>($@"
            SELECT {MsgCols} FROM app.ChatMessages m
            LEFT JOIN app.Users u ON u.UserId = m.UserID
            WHERE m.CompanyID = @CompanyID AND m.ParentMessageID = @Parent AND m.IsDeleted = 0
            ORDER BY m.CreatedAt ASC",
            new { CompanyID = companyId, Parent = parentMessageId });
    }

    public async Task<ChatMessageDto?> UpdateReactionsAsync(int companyId, long messageId, string? reactionsJson)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("UPDATE app.ChatMessages SET Reactions=@Reactions WHERE MessageID=@MessageID AND CompanyID=@CompanyID",
            new { Reactions = reactionsJson, MessageID = messageId, CompanyID = companyId });
        return await GetMessageAsync(companyId, messageId);
    }

    // ── Read receipts ─────────────────────────────────────────────────────
    public async Task MarkReadAsync(int companyId, long roomId, long userId, long lastReadMessageId)
    {
        await using var c = await _db.OpenAsync();
        var json = await c.ExecuteScalarAsync<string?>("SELECT Participants FROM app.ChatRooms WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { RoomID = roomId, CompanyID = companyId });
        var participants = ParseParticipants(json);
        var me = participants.FirstOrDefault(p => p.userId == userId);
        if (me == null)
        {
            me = new ChatParticipant { userId = userId, role = "Member", isMuted = false, joinedAt = DateTime.UtcNow.ToString("o") };
            participants.Add(me);
        }
        me.lastReadMessageId = lastReadMessageId;
        me.lastReadAt = DateTime.UtcNow.ToString("o");
        await c.ExecuteAsync("UPDATE app.ChatRooms SET Participants=@Participants WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { Participants = JsonSerializer.Serialize(participants), RoomID = roomId, CompanyID = companyId });
    }

    public async Task<IEnumerable<UnreadCountDto>> GetUnreadCountsAsync(int companyId, long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<UnreadCountDto>($@"
            SELECT r.RoomID, UnreadCount = {UnreadExpr}
            FROM app.ChatRooms r
            WHERE r.CompanyID = @CompanyID AND r.IsDeleted = 0
              AND EXISTS (SELECT 1 FROM OPENJSON(ISNULL(r.Participants,'[]')) p WHERE JSON_VALUE(p.value,'$.userId') = CAST(@UserID AS NVARCHAR(20)))",
            new { CompanyID = companyId, UserID = userId });
    }

    // ── Search & contacts ─────────────────────────────────────────────────
    public async Task<IEnumerable<ChatMessageDto>> SearchAsync(int companyId, long userId, string query, int limit)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<ChatMessageDto>($@"
            SELECT TOP (@Limit) {MsgCols}, r.Name AS RoomName, r.Type AS RoomType
            FROM app.ChatMessages m
            JOIN app.ChatRooms r ON r.RoomID = m.RoomID
            LEFT JOIN app.Users u ON u.UserId = m.UserID
            WHERE m.CompanyID = @CompanyID AND m.IsDeleted = 0 AND m.Content LIKE @Q
              AND EXISTS (SELECT 1 FROM OPENJSON(ISNULL(r.Participants,'[]')) p WHERE JSON_VALUE(p.value,'$.userId') = CAST(@UserID AS NVARCHAR(20)))
            ORDER BY m.CreatedAt DESC",
            new { CompanyID = companyId, UserID = userId, Q = "%" + query + "%", Limit = limit });
    }

    public async Task<IEnumerable<ChatContactDto>> GetContactsAsync(int companyId, long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<ChatContactDto>(@"
            SELECT UserId AS UserID, FullName AS UserName, Role AS Designation, Email
            FROM app.Users
            WHERE CompanyId = @CompanyID AND UserId <> @UserID AND IsActive = 1 AND ISNULL(IsDeletedTransaction,0) = 0
            ORDER BY FullName",
            new { CompanyID = companyId, UserID = userId });
    }

    public async Task<string?> GetUserNameAsync(int companyId, long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<string?>("SELECT FullName FROM app.Users WHERE UserId=@UserID AND CompanyId=@CompanyID", new { UserID = userId, CompanyID = companyId });
    }

    private static List<ChatParticipant> ParseParticipants(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<ChatParticipant>>(json) ?? new(); }
        catch { return new(); }
    }

    // ── Group management (members, admin, read-only, delete) ───────────────

    private static async Task<List<ChatParticipant>> LoadParticipantsAsync(SqlConnection c, int companyId, long roomId)
    {
        var json = await c.ExecuteScalarAsync<string?>(
            "SELECT Participants FROM app.ChatRooms WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { RoomID = roomId, CompanyID = companyId });
        return ParseParticipants(json);
    }

    private static async Task SaveParticipantsAsync(SqlConnection c, int companyId, long roomId, List<ChatParticipant> ps)
    {
        await c.ExecuteAsync(
            "UPDATE app.ChatRooms SET Participants=@P, UpdatedDate=SYSUTCDATETIME() WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { P = JsonSerializer.Serialize(ps), RoomID = roomId, CompanyID = companyId });
    }

    private static bool IsAdminRole(string? role)
        => string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>The user's role in the room ("Owner"/"Admin"/"Member"), or null if not a member.</summary>
    public async Task<string?> GetMemberRoleAsync(int companyId, long roomId, long userId)
    {
        await using var c = await _db.OpenAsync();
        var ps = await LoadParticipantsAsync(c, companyId, roomId);
        return ps.FirstOrDefault(p => p.userId == userId)?.role;
    }

    public async Task<bool> IsRoomAdminAsync(int companyId, long roomId, long userId)
        => IsAdminRole(await GetMemberRoleAsync(companyId, roomId, userId));

    public async Task<bool> IsRoomReadOnlyAsync(int companyId, long roomId)
    {
        await using var c = await _db.OpenAsync();
        return (await c.ExecuteScalarAsync<bool?>(
            "SELECT IsReadOnly FROM app.ChatRooms WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { RoomID = roomId, CompanyID = companyId })) ?? false;
    }

    public async Task<long?> GetRoomCreatedByAsync(int companyId, long roomId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<long?>(
            "SELECT CreatedBy FROM app.ChatRooms WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { RoomID = roomId, CompanyID = companyId });
    }

    /// <summary>Add members (skips users already in the room). Returns the names actually added.</summary>
    public async Task<List<string>> AddMembersAsync(int companyId, long roomId, IEnumerable<ChatParticipant> members)
    {
        await using var c = await _db.OpenAsync();
        var ps = await LoadParticipantsAsync(c, companyId, roomId);
        var added = new List<string>();
        foreach (var m in members)
        {
            if (m.userId <= 0) continue;
            var ex = ps.FirstOrDefault(p => p.userId == m.userId);
            if (ex != null)
            {
                // Re-adding someone who had LEFT → clear their left/hidden state so they rejoin as an
                // active Member (a fresh joinedAt). Already-active members are skipped.
                if (!string.IsNullOrEmpty(ex.leftAt) || !string.IsNullOrEmpty(ex.hiddenAt))
                {
                    ex.leftAt = null; ex.hiddenAt = null; ex.role = "Member"; ex.joinedAt = DateTime.UtcNow.ToString("o");
                    added.Add(ex.userName ?? m.userName ?? "");
                }
                continue;
            }
            ps.Add(new ChatParticipant { userId = m.userId, userName = m.userName, role = "Member", isMuted = false, joinedAt = DateTime.UtcNow.ToString("o") });
            added.Add(m.userName ?? "");
        }
        if (added.Count > 0) await SaveParticipantsAsync(c, companyId, roomId, ps);
        return added;
    }

    /// <summary>Remove a member from the room. Returns the removed member's name (or null).</summary>
    public async Task<string?> RemoveMemberAsync(int companyId, long roomId, long targetUserId)
    {
        await using var c = await _db.OpenAsync();
        var ps = await LoadParticipantsAsync(c, companyId, roomId);
        var target = ps.FirstOrDefault(p => p.userId == targetUserId);
        if (target == null) return null;

        // Soft-leave (WhatsApp-style): mark them a PAST member instead of hard-removing — they keep the
        // group read-only in their list (history up to now) and show under "Past members". Demote to Member.
        target.leftAt = DateTime.UtcNow.ToString("o");
        target.role = "Member";

        // Safety net: a group must never be left with active members but NO active admin/owner (it would
        // become unmanageable, and if "only admins can send" is on, no one could ever post). If the one
        // who left was the last active admin, promote the earliest-joined remaining ACTIVE member.
        static bool IsAdmin(string? r) => string.Equals(r, "Owner", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase);
        var active = ps.Where(p => string.IsNullOrEmpty(p.leftAt)).ToList();
        if (active.Count > 0 && !active.Any(p => IsAdmin(p.role)))
            active.OrderBy(p => p.joinedAt ?? "").First().role = "Admin";

        await SaveParticipantsAsync(c, companyId, roomId, ps);
        return target.userName;
    }

    /// <summary>Set the caller's per-user chat prefs (mute / pin / archive) on their participant entry.
    /// Only the passed (non-null) flags change. Per-user — never affects other members.</summary>
    public async Task SetChatPrefsAsync(int companyId, long roomId, long userId, bool? mute, bool? pin, bool? archive)
    {
        await using var c = await _db.OpenAsync();
        var ps = await LoadParticipantsAsync(c, companyId, roomId);
        var me = ps.FirstOrDefault(p => p.userId == userId);
        if (me == null) return;
        if (mute.HasValue) me.isMuted = mute.Value;
        if (pin.HasValue) me.isPinned = pin.Value;
        if (archive.HasValue) me.archivedAt = archive.Value ? DateTime.UtcNow.ToString("o") : null;
        await SaveParticipantsAsync(c, companyId, roomId, ps);
    }

    /// <summary>"Delete group for me": hide the room from ONE user's list (sets their hiddenAt). The
    /// group + its messages stay for everyone else — nothing is deleted globally. Used after leaving.</summary>
    public async Task DeleteForMeAsync(int companyId, long roomId, long userId)
    {
        await using var c = await _db.OpenAsync();
        var ps = await LoadParticipantsAsync(c, companyId, roomId);
        var me = ps.FirstOrDefault(p => p.userId == userId);
        if (me == null) return;
        me.hiddenAt = DateTime.UtcNow.ToString("o");
        await SaveParticipantsAsync(c, companyId, roomId, ps);
    }

    /// <summary>Promote/demote a member ("Admin" or "Member").</summary>
    public async Task SetMemberRoleAsync(int companyId, long roomId, long targetUserId, string role)
    {
        await using var c = await _db.OpenAsync();
        var ps = await LoadParticipantsAsync(c, companyId, roomId);
        var target = ps.FirstOrDefault(p => p.userId == targetUserId);
        if (target != null && !string.Equals(target.role, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            target.role = role;
            await SaveParticipantsAsync(c, companyId, roomId, ps);
        }
    }

    /// <summary>Toggle "only admins can send" (IsReadOnly) for a group.</summary>
    public async Task SetReadOnlyAsync(int companyId, long roomId, bool isReadOnly)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync(
            "UPDATE app.ChatRooms SET IsReadOnly=@RO, UpdatedDate=SYSUTCDATETIME() WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { RO = isReadOnly, RoomID = roomId, CompanyID = companyId });
    }

    /// <summary>Soft-delete a group/channel.</summary>
    public async Task DeleteRoomAsync(int companyId, long roomId)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync(
            "UPDATE app.ChatRooms SET IsDeleted=1, UpdatedDate=SYSUTCDATETIME() WHERE RoomID=@RoomID AND CompanyID=@CompanyID",
            new { RoomID = roomId, CompanyID = companyId });
    }

    // ── Last-seen (WhatsApp-style presence timestamp) ──────────────────────
    /// <summary>Stamp the user's last-active time (called on hub connect + disconnect). Returns the UTC time set.</summary>
    public async Task<DateTime> SetLastSeenAsync(long userId)
    {
        var now = DateTime.UtcNow;
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("UPDATE app.Users SET LastSeenAt=@now WHERE UserId=@userId", new { now, userId });
        return now;
    }

    public async Task<DateTime?> GetLastSeenAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<DateTime?>("SELECT LastSeenAt FROM app.Users WHERE UserId=@userId", new { userId });
    }
}
