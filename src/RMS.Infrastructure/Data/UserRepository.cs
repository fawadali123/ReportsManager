using System.Data.Common;
using RMS.Infrastructure.Data.Providers;
using RMS.Core.Abstractions;
using RMS.Core.Domain;

namespace RMS.Infrastructure.Data;

internal sealed class UserRepository(AppDatabase db) : IUserRepository
{
    private const string Select = "SELECT ID, CODE, FULL_NAME, EMAIL, AD_USERNAME, ACTIVE, ROLE FROM RMS_USERS";

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default) =>
        await db.QueryAsync($"{Select} ORDER BY UPPER(CODE)", Map, ct);

    public Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.QuerySingleAsync($"{Select} WHERE ID = @p_id", Map, ct, P.Int("p_id", id));

    public Task<AppUser?> GetByAdUsernameAsync(string adUsername, CancellationToken ct = default) =>
        db.QuerySingleAsync($"{Select} WHERE UPPER(AD_USERNAME) = UPPER(@p_ad_username)", Map, ct,
            P.Text("p_ad_username", adUsername.Trim()));

    public Task<int> CreateAsync(AppUser user, CancellationToken ct = default) =>
        db.InsertAsync(
            """
            INSERT INTO RMS_USERS (CODE, FULL_NAME, EMAIL, AD_USERNAME, ACTIVE, ROLE)
            VALUES (@p_code, @p_full_name, @p_email, @p_ad_username, @p_active, @p_role)
            """,
            ct, Values(user));

    public Task UpdateAsync(AppUser user, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE RMS_USERS
               SET CODE = @p_code, FULL_NAME = @p_full_name, EMAIL = @p_email, AD_USERNAME = @p_ad_username,
                   ACTIVE = @p_active, ROLE = @p_role, UPDATED_AT = {now}
             WHERE ID = @p_id
            """,
            ct, [.. Values(user), P.Int("p_id", user.Id)]);

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        db.ExecuteAsync("DELETE FROM RMS_USERS WHERE ID = @p_id", ct, P.Int("p_id", id));

    private static DbParam[] Values(AppUser user) =>
    [
        P.Text("p_code", user.Code.Trim()),
        P.Text("p_full_name", user.FullName?.Trim()),
        P.Text("p_email", user.Email?.Trim()),
        P.Text("p_ad_username", user.AdUsername.Trim()),
        P.Bool("p_active", user.Active),
        P.Text("p_role", Roles.Normalize(user.Role)),
    ];

    private static AppUser Map(DbDataReader r) => new()
    {
        Id = r.Int("ID"),
        Code = r.Text("CODE"),
        FullName = r.TextOrNull("FULL_NAME"),
        Email = r.TextOrNull("EMAIL"),
        AdUsername = r.Text("AD_USERNAME"),
        Active = r.Bool("ACTIVE"),
        Role = Roles.Normalize(r.Text("ROLE")),
    };
}
