using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RMS.Core.Abstractions;
using RMS.Core.Scheduling;
using RMS.Core.Services;

namespace RMS.Infrastructure.Delivery;

/// <summary>Bound from the "FileShare" configuration section of the scheduler service.</summary>
public sealed class FileShareOptions
{
    /// <summary>
    /// Optional account for writing to the shares, as DOMAIN\user or user@domain. Leave empty to write as the
    /// account the Windows service runs under, which is the usual set-up: give that account Modify rights on the folder.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>Use "env:VARIABLE_NAME" to keep it out of the file.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// Saves report files to a folder, normally a UNC share. The file is written under a temporary name and renamed
/// when complete, so nobody picks up half a file.
/// </summary>
internal sealed class FileShareWriter(
    IOptions<FileShareOptions> options,
    SchedulingOptions scheduling,
    ILogger<FileShareWriter> logger) : IFileShareWriter
{
    private readonly FileShareOptions _options = options.Value;

    public async Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default)
    {
        if (ShareFolderRules.Check(folder, scheduling.AllowedShareRoots) is { } problem)
        {
            throw new FileDeliveryException(problem);
        }

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.Contains('\\') || fileName.Contains('/'))
        {
            throw new FileDeliveryException($"\"{fileName}\" is not a valid file name.");
        }

        folder = folder.Trim();
        using var connection = ShareConnection.Open(folder, _options, logger);

        try
        {
            Directory.CreateDirectory(folder);

            var partial = Path.Combine(folder, $".{Guid.NewGuid():N}.partial");
            try
            {
                await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await content.CopyToAsync(output, ct);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }

            var target = Path.Combine(folder, fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var copy = 2; ; copy++)
            {
                try
                {
                    File.Move(partial, target, overwrite: false);
                    return target;
                }
                catch (IOException) when (File.Exists(target) && copy < 1000)
                {
                    target = Path.Combine(folder, $"{stem} ({copy}){extension}");
                }
                catch
                {
                    TryDelete(partial);
                    throw;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.LogWarning(ex, "Could not save {FileName} to {Folder}", fileName, folder);
            var reason = ex is UnauthorizedAccessException
                ? $"access to {folder} was denied. Give the account the scheduler service runs as ({Environment.UserDomainName}\\{Environment.UserName}) Modify rights on the folder, or set FileShare:Username."
                : ex.Message;
            throw new FileDeliveryException("The file could not be saved: " + reason, ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// When FileShare:Username is set, connects to the \\server\share with those credentials for the duration of
    /// the write (WNetAddConnection2, the same as "net use" without a drive letter). Windows only.
    /// </summary>
    private sealed class ShareConnection : IDisposable
    {
        private const int NoError = 0;
        private const int ErrorSessionCredentialConflict = 1219;
        private const int ErrorAlreadyAssigned = 85;
        private const int ResourceTypeDisk = 1;

        private readonly string? _remoteName;

        private ShareConnection(string? remoteName) => _remoteName = remoteName;

        public static ShareConnection Open(string folder, FileShareOptions options, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(options.Username))
            {
                return new ShareConnection(null);
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new FileDeliveryException("FileShare:Username only works when the scheduler runs on Windows.");
            }

            var root = ShareRoot(folder)
                ?? throw new FileDeliveryException("FileShare:Username only applies to network paths such as \\\\server\\share\\folder.");

            var resource = new NetResource { Type = ResourceTypeDisk, RemoteName = root };
            var result = WNetAddConnection2(ref resource, options.Password, options.Username, 0);
            switch (result)
            {
                case NoError:
                    return new ShareConnection(root);
                case ErrorAlreadyAssigned:
                    return new ShareConnection(null);
                case ErrorSessionCredentialConflict:
                    logger.LogWarning("{Share} is already connected with other credentials; writing with those", root);
                    return new ShareConnection(null);
                default:
                    throw new FileDeliveryException(
                        $"Could not connect to {root} as {options.Username}: {new Win32Exception(result).Message}");
            }
        }

        public void Dispose()
        {
            if (_remoteName is not null && OperatingSystem.IsWindows())
            {
                _ = WNetCancelConnection2(_remoteName, 0, true);
            }
        }

        /// <summary>\\server\share from \\server\share\folder\sub.</summary>
        private static string? ShareRoot(string folder)
        {
            var normalized = ShareFolderRules.Normalize(folder);
            if (normalized is null || !normalized.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return null;
            }

            var parts = normalized[2..].Split('\\');
            return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : null;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NetResource
        {
            public int Scope;
            public int Type;
            public int DisplayType;
            public int Usage;
            public string? LocalName;
            public string? RemoteName;
            public string? Comment;
            public string? Provider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetAddConnection2W")]
        private static extern int WNetAddConnection2(ref NetResource resource, string? password, string? username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetCancelConnection2W")]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);
    }
}
