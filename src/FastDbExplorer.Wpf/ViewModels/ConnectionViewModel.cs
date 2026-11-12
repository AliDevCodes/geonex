using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Domain;
using FastDbExplorer.Wpf.Localization;

namespace FastDbExplorer.Wpf.ViewModels;

/// <summary>
/// Named profile management and connection creation. Everything, including the SQL password, is entered in the one
/// profile form (no popup). A saved password goes to the encrypted credential vault and is never shown again;
/// connecting and switching profiles read it from the vault without asking.
/// </summary>
public sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly IDatabaseMetadataService _metadata;
    private readonly IConnectionProfileStore _store;
    private readonly ICredentialVault _vault;
    private readonly ISqlWriteAccessProbe _writeAccessProbe;
    private CancellationTokenSource? _connectCts;

    public ConnectionViewModel(IDatabaseMetadataService metadata, IConnectionProfileStore store, ICredentialVault vault,
        ISqlWriteAccessProbe writeAccessProbe)
    {
        _metadata = metadata;
        _store = store;
        _vault = vault;
        _writeAccessProbe = writeAccessProbe;
    }

    public event Action<ConnectionSettings, IReadOnlyList<DatabaseInfo>, SavedConnection>? Connected;

    /// <summary>The connection works, but its login can change data. The app stays read-only; the user is told.</summary>
    public event Action? WriteAccessDetected;
    public event Action<SavedConnection>? ProfileDeleted;

    /// <summary>True after a failed connect that the user fixes in the Connections form (password missing or rejected).</summary>
    public bool NeedsPasswordInput { get; private set; }

    public ObservableCollection<SavedConnection> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _profileName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _server = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    [NotifyPropertyChangedFor(nameof(UseWindowsAuth))]
    private bool _useSqlAuth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _userName = "";

    /// <summary>The password being typed (new profile, or "change password"). Transient: cleared after save and on selection change.</summary>
    [ObservableProperty] private string _password = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordMasked))]
    [NotifyPropertyChangedFor(nameof(IsPasswordRevealed))]
    private bool _showPassword;

    /// <summary>The selected profile already has an encrypted password in the vault.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStoredPasswordLocked))]
    [NotifyPropertyChangedFor(nameof(IsPasswordEditable))]
    [NotifyPropertyChangedFor(nameof(IsPasswordMasked))]
    [NotifyPropertyChangedFor(nameof(IsPasswordRevealed))]
    private bool _hasStoredPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStoredPasswordLocked))]
    [NotifyPropertyChangedFor(nameof(IsPasswordEditable))]
    [NotifyPropertyChangedFor(nameof(IsPasswordMasked))]
    [NotifyPropertyChangedFor(nameof(IsPasswordRevealed))]
    private bool _isChangingPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedCommand))]
    private SavedConnection? _selectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _trustServerCertificate = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string _errorMessage = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = "";

    public bool UseWindowsAuth
    {
        get => !UseSqlAuth;
        set => UseSqlAuth = !value;
    }

    // Password field states: locked mask + "change" button (stored), or editable input with the show/hide toggle.
    public bool IsStoredPasswordLocked => HasStoredPassword && !IsChangingPassword;
    public bool IsPasswordEditable => !IsStoredPasswordLocked;
    public bool IsPasswordMasked => IsPasswordEditable && !ShowPassword;
    public bool IsPasswordRevealed => IsPasswordEditable && ShowPassword;

    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage)
        && StatusMessage != Strings.Cancelled && StatusMessage != Strings.Connecting;

    public async Task InitializeAsync() => await LoadProfilesAsync();

    private bool CanSaveProfile() => !IsBusy
        && !string.IsNullOrWhiteSpace(ProfileName)
        && !string.IsNullOrWhiteSpace(Server)
        && (!UseSqlAuth || !string.IsNullOrWhiteSpace(UserName));

    private bool CanConnectSelected() => !IsBusy && SelectedProfile is not null;

    [RelayCommand]
    private void NewProfile()
    {
        SelectedProfile = null;
        ProfileName = "";
        Server = "";
        UseSqlAuth = false;
        UserName = "";
        TrustServerCertificate = true;
        HasStoredPassword = false;
        ResetPasswordInput();
        ErrorMessage = "";
        StatusMessage = "";
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => ShowPassword = !ShowPassword;

    [RelayCommand]
    private void ChangePassword()
    {
        Password = "";
        ShowPassword = false;
        IsChangingPassword = true;
    }

    [RelayCommand]
    private void CancelPasswordChange() => ResetPasswordInput();

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private Task SaveProfileAsync() => SaveProfileCoreAsync();

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile || IsBusy) return;
        try
        {
            await _store.RemoveAsync(profile);
            await ForgetStoredPasswordAsync(profile.Id);
            Profiles.Remove(profile);
            ProfileDeleted?.Invoke(profile);
            NewProfile();
            StatusMessage = ShellStrings.ProfileDeleted;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnectSelected))]
    private async Task ConnectSelectedAsync()
    {
        var profile = await SaveProfileCoreAsync();
        if (profile is not null) await ConnectProfileAsync(profile);
    }

    [RelayCommand]
    private void Cancel() => _connectCts?.Cancel();

    public async Task<bool> ConnectProfileAsync(SavedConnection profile)
    {
        NeedsPasswordInput = false;
        var saved = Profiles.FirstOrDefault(item => item.Id == profile.Id);
        if (saved is null)
        {
            ErrorMessage = ShellStrings.ProfileUnavailable;
            return false;
        }

        SelectedProfile = saved;
        _connectCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _connectCts = cts;
        IsBusy = true;
        ErrorMessage = "";
        StatusMessage = Strings.Connecting;
        try
        {
            var (ready, password) = await ResolvePasswordAsync(saved, cts.Token);
            if (!ready) return false;

            var settings = new ConnectionSettings(
                saved.Server,
                saved.Authentication,
                saved.UserName,
                password,
                saved.TrustServerCertificate);
            var databases = await _metadata.GetDatabasesAsync(settings, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            StatusMessage = Strings.TestOk;
            var canWrite = await _writeAccessProbe.CanWriteAsync(settings, cts.Token);
            Connected?.Invoke(settings, databases, saved);
            if (canWrite == true) WriteAccessDetected?.Invoke();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (_connectCts == cts) StatusMessage = Strings.Cancelled;
            return false;
        }
        catch (DatabaseAccessException ex)
        {
            await ReportConnectFailureAsync(saved, ex, cts);
            return false;
        }
        catch (Exception ex)
        {
            if (_connectCts == cts)
            {
                StatusMessage = "";
                ErrorMessage = ex.Message;
            }
            return false;
        }
        finally
        {
            if (_connectCts == cts)
            {
                _connectCts = null;
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// The password a connection needs. Windows logins need none. A SQL login uses its stored password; when none is
    /// readable, the form asks for it instead (no popup) and the attempt is not ready.
    /// </summary>
    private async Task<(bool Ready, string? Password)> ResolvePasswordAsync(SavedConnection saved, CancellationToken ct)
    {
        if (saved.Authentication != AuthenticationMode.SqlServer) return (true, null);

        var password = saved.RememberPassword ? await ReadStoredPasswordAsync(saved.Id, ct) : null;
        if (password is not null) return (true, password);

        if (saved.RememberPassword) await DropStoredPasswordAsync(saved); // unreadable entry (e.g. other Windows account)
        NeedsPasswordInput = true;
        StatusMessage = "";
        ErrorMessage = CredentialStrings.PasswordRequired;
        return (false, null);
    }

    /// <summary>
    /// Shows a failed attempt. A login failure with a stored password discards that password and asks for a new one.
    /// A superseded attempt drops a rejected password but shows nothing.
    /// </summary>
    private async Task ReportConnectFailureAsync(SavedConnection saved, DatabaseAccessException ex, CancellationTokenSource cts)
    {
        // The stored password was read from the vault, so a login failure means it is wrong or outdated: discard it.
        var rejected = saved.Authentication == AuthenticationMode.SqlServer && ex.Kind == DatabaseErrorKind.LoginFailed;
        if (rejected) await DropStoredPasswordAsync(saved);
        if (_connectCts != cts) return;

        StatusMessage = "";
        NeedsPasswordInput = rejected;
        ErrorMessage = rejected
            ? $"{Strings.Describe(ex)} {CredentialStrings.StoredPasswordRejected}"
            : Strings.Describe(ex);
    }

    private async Task<SavedConnection?> SaveProfileCoreAsync()
    {
        if (!CanSaveProfile())
        {
            ErrorMessage = ShellStrings.ProfileFieldsRequired;
            return null;
        }

        var previous = SelectedProfile;
        var profileId = previous?.Id ?? Guid.NewGuid();
        if (Profiles.Any(item => item.Id != profileId
            && string.Equals(item.Name, ProfileName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = ShellStrings.ProfileNameTaken;
            return null;
        }

        var draft = new SavedConnection(
            Server.Trim(),
            UseSqlAuth ? AuthenticationMode.SqlServer : AuthenticationMode.Windows,
            UseSqlAuth ? UserName.Trim() : null,
            ProfileName.Trim(),
            TrustServerCertificate,
            profileId);

        // A stored password belongs to one server + account. Keep it only if the target is unchanged;
        // a newly typed password always replaces it.
        var newPassword = draft.Authentication == AuthenticationMode.SqlServer && Password.Length > 0 ? Password : null;
        var hadStored = previous is { RememberPassword: true };
        var targetChanged = IdentityChanged(previous, draft);
        var keepStored = draft.Authentication == AuthenticationMode.SqlServer && hadStored && !targetChanged;
        var profile = draft with { RememberPassword = newPassword is not null || keepStored };

        try
        {
            // Vault first: the profile flag is only saved when the vault write succeeded.
            if (newPassword is not null) await _vault.SavePasswordAsync(profileId, newPassword);
            else if (!profile.RememberPassword) await _vault.RemoveAsync(profileId);
        }
        catch (Exception ex)
        {
            ErrorMessage = CredentialStrings.PasswordStoreFailed + ex.Message;
            return null;
        }

        try
        {
            await _store.SaveAsync(profile);
            ResetPasswordInput(); // the typed password must not stay in memory/UI after saving
            await LoadProfilesAsync(profile.Id);
            StatusMessage = hadStored && targetChanged && newPassword is null
                ? $"{ShellStrings.ProfileSaved} {CredentialStrings.PasswordClearedNote}"
                : ShellStrings.ProfileSaved;
            ErrorMessage = "";
            return Profiles.First(item => item.Id == profile.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    private static bool IdentityChanged(SavedConnection? previous, SavedConnection current) =>
        previous is not null
        && (!string.Equals(previous.Server, current.Server, StringComparison.OrdinalIgnoreCase)
            || previous.Authentication != current.Authentication
            || !string.Equals(previous.UserName, current.UserName, StringComparison.OrdinalIgnoreCase));

    private void ResetPasswordInput()
    {
        Password = "";
        ShowPassword = false;
        IsChangingPassword = false;
    }

    private async Task<string?> ReadStoredPasswordAsync(Guid profileId, CancellationToken ct)
    {
        try { return await _vault.GetPasswordAsync(profileId, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; } // an unreadable vault only means "enter it again in the form"
    }

    private async Task ForgetStoredPasswordAsync(Guid profileId)
    {
        try { await _vault.RemoveAsync(profileId); }
        catch (Exception) { /* an unreadable entry cannot be shown or used anyway */ }
    }

    /// <summary>Removes a missing/rejected password and unlocks the form so a new one can be typed.</summary>
    private async Task DropStoredPasswordAsync(SavedConnection profile)
    {
        try
        {
            await _vault.RemoveAsync(profile.Id);
            await _store.SaveAsync(profile with { RememberPassword = false });
            await LoadProfilesAsync(profile.Id);
        }
        catch (Exception) { /* best effort: the user can still type a new password */ }
    }

    private async Task LoadProfilesAsync(Guid? selectId = null)
    {
        var requestedId = selectId ?? SelectedProfile?.Id;
        var items = await _store.LoadAsync();
        SelectedProfile = null;
        Profiles.Clear();
        foreach (var item in items) Profiles.Add(item);

        SelectedProfile = requestedId is null
            ? Profiles.FirstOrDefault()
            : Profiles.FirstOrDefault(item => item.Id == requestedId.Value) ?? Profiles.FirstOrDefault();
    }

    partial void OnSelectedProfileChanged(SavedConnection? value)
    {
        if (value is null) return;
        ProfileName = value.Name;
        Server = value.Server;
        UseSqlAuth = value.Authentication == AuthenticationMode.SqlServer;
        UserName = value.UserName ?? "";
        TrustServerCertificate = value.TrustServerCertificate;
        HasStoredPassword = value.RememberPassword && value.Authentication == AuthenticationMode.SqlServer;
        ResetPasswordInput();
        ErrorMessage = "";
        StatusMessage = "";
    }
}
