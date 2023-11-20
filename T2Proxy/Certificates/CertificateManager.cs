using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Helpers;
using T2Proxy.Network.Certificate;
using T2Proxy.Shared;

namespace T2Proxy.Network;

public enum CertificateEngine
{
    BouncyCastle = 0,

    BouncyCastleFast = 2,

    DefaultWindows = 1
}

public sealed class CertificateManager : IDisposable
{
    private const string DefaultRootCertificateIssuer = "T2";

    private const string DefaultRootRootCertificateName = "T2Proxy Root Certification Authority";

    private static readonly ConcurrentDictionary<string, object> _saveCertificateLocks = new();

    private readonly ConcurrentDictionary<string, CachedCertificate> cachedCertificates = new();

    private readonly CancellationTokenSource clearCertificatesTokenSource = new();

    private readonly SemaphoreSlim pendingCertificateCreationTaskLock = new(1);

    private readonly Dictionary<string, Task<X509Certificate2?>> pendingCertificateCreationTasks = new();

    private readonly object rootCertCreationLock = new();

    private ICertificateMaker? certEngineValue;

    private ICertificateCache certificateCache = new DefaultCertificateDiskCache();

    private bool disposed;

    private CertificateEngine engine;

    private string? issuer;

    private X509Certificate2? rootCertificate;

    private string? rootCertificateName;

    internal CertificateManager(string? rootCertificateName, string? rootCertificateIssuerName,
        bool userTrustRootCertificate, bool machineTrustRootCertificate, bool trustRootCertificateAsAdmin,
        ExceptionHandler? exceptionFunc)
    {
        ExceptionFunc = exceptionFunc;

        UserTrustRoot = userTrustRootCertificate || machineTrustRootCertificate;

        MachineTrustRoot = machineTrustRootCertificate;
        TrustRootAsAdministrator = trustRootCertificateAsAdmin;

        if (rootCertificateName != null) RootCertificateName = rootCertificateName;

        if (rootCertificateIssuerName != null) RootCertificateIssuerName = rootCertificateIssuerName;

        CertificateEngine = CertificateEngine.BouncyCastle;
    }

    private ICertificateMaker CertEngine
    {
        get
        {
            if (certEngineValue == null)
                switch (engine)
                {
                    case CertificateEngine.BouncyCastle:
                        certEngineValue = new BcCertificateMaker(ExceptionFunc, CertificateValidDays);
                        break;
                    case CertificateEngine.BouncyCastleFast:
                        certEngineValue = new BcCertificateMakerFast(ExceptionFunc, CertificateValidDays);
                        break;
                    case CertificateEngine.DefaultWindows:
                    default:
                        certEngineValue = new WinCertificateMaker(ExceptionFunc, CertificateValidDays);
                        break;
                }

            return certEngineValue;
        }
    }
    internal bool CertValidated => RootCertificate != null;

    internal bool UserTrustRoot { get; set; }

    internal bool MachineTrustRoot { get; set; }

    internal bool TrustRootAsAdministrator { get; set; }

    internal ExceptionHandler? ExceptionFunc { get; set; }

    public CertificateEngine CertificateEngine
    {
        get => engine;
        set
        {
            if (!RunTime.IsWindows) value = CertificateEngine.BouncyCastle;

            if (value != engine)
            {
                certEngineValue = null!;
                engine = value;
            }
        }
    }
    public string PfxPassword { get; set; } = string.Empty;

    public string PfxFilePath { get; set; } = string.Empty;

    public int CertificateValidDays { get; set; } = 365;

 
    public string RootCertificateIssuerName
    {
        get => issuer ?? DefaultRootCertificateIssuer;
        set => issuer = value;
    }

    public string RootCertificateName
    {
        get => rootCertificateName ?? DefaultRootRootCertificateName;
        set => rootCertificateName = value;
    }

    public X509Certificate2? RootCertificate
    {
        get => rootCertificate;
        set
        {
            ClearRootCertificate();
            rootCertificate = value;
        }
    }
    public bool SaveFakeCertificates { get; set; } = false;

    public ICertificateCache CertificateStorage
    {
        get => certificateCache;
        set => certificateCache = value ?? new DefaultCertificateDiskCache();
    }

    public bool OverwritePfxFile { get; set; } = true;

    public int CertificateCacheTimeOutMinutes { get; set; } = 60;

    public X509KeyStorageFlags StorageFlag { get; set; } = X509KeyStorageFlags.Exportable;

    public bool DisableWildCardCertificates { get; set; } = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

   
    private bool RootCertificateInstalled(StoreLocation storeLocation)
    {
        if (RootCertificate == null) throw new Exception("Root certificate is null.");

        var value = $"{RootCertificate.Issuer}";
        return FindCertificates(StoreName.Root, storeLocation, value).Count > 0
               && (CertificateEngine != CertificateEngine.DefaultWindows
                   || FindCertificates(StoreName.My, storeLocation, value).Count > 0);
    }

    private static X509Certificate2Collection FindCertificates(StoreName storeName, StoreLocation storeLocation,
        string findValue)
    {
        var x509Store = new X509Store(storeName, storeLocation);
        try
        {
            x509Store.Open(OpenFlags.OpenExistingOnly);
            return x509Store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, findValue, false);
        }
        finally
        {
            x509Store.Close();
        }
    }

    private void InstallCertificate(StoreName storeName, StoreLocation storeLocation)
    {
        if (RootCertificate == null) throw new Exception("Could not install certificate as it is null or empty.");

        var x509Store = new X509Store(storeName, storeLocation);

        try
        {
            x509Store.Open(OpenFlags.ReadWrite);
            x509Store.Add(RootCertificate);
        }
        catch (Exception e)
        {
            OnException(
                new Exception("Failed to make system trust root certificate "
                              + $" for {storeName}\\{storeLocation} store location. You may need admin rights.",e));
        }
        finally
        {
            x509Store.Close();
        }
    }

    private void UninstallCertificate(StoreName storeName, StoreLocation storeLocation, X509Certificate2? certificate)
    {
        if (certificate == null)
        {
            OnException(new Exception("Could not remove certificate as it is null or empty."));
            return;
        }

        var x509Store = new X509Store(storeName, storeLocation);

        try
        {
            x509Store.Open(OpenFlags.ReadWrite);

            x509Store.Remove(certificate);
        }
        catch (Exception e)
        {
            OnException(new Exception("Failed to remove root certificate trust "
                                      + $" for {storeLocation} store location. You may need admin rights.", e));
        }
        finally
        {
            x509Store.Close();
        }
    }

    private X509Certificate2 MakeCertificate(string certificateName, bool isRootCertificate)
    {
        if (!isRootCertificate && RootCertificate == null) CreateRootCertificate();
        var certificate = CertEngine.MakeCertificate(certificateName, isRootCertificate ? null : RootCertificate);
        if (CertificateEngine == CertificateEngine.DefaultWindows)
            Task.Run(() => UninstallCertificate(StoreName.My, StoreLocation.CurrentUser, certificate));

        return certificate;
    }

    private void OnException(Exception exception)
    {
        ExceptionFunc?.Invoke(exception);
    }

    internal X509Certificate2? CreateCertificate(string certificateName, bool isRootCertificate)
    {
        X509Certificate2? certificate;
        try
        {
            if (!isRootCertificate && SaveFakeCertificates)
            {
                var subjectName = ServerConstants.CnRemoverRegex
                    .Replace(certificateName, string.Empty)
                    .Replace("*", "$x$");

                try
                {
                    certificate = certificateCache.LoadCertificate(subjectName, StorageFlag);

                    if (certificate != null && certificate.NotAfter <= DateTime.Now)
                    {
                        OnException(new Exception($"Cached certificate for {subjectName} has expired."));
                        certificate = null;
                    }
                }
                catch (Exception e)
                {
                    OnException(new Exception("Failed to load fake certificate.", e));
                    certificate = null;
                }

                if (certificate == null)
                {
                    certificate = MakeCertificate(certificateName, false);

                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var lockKey = subjectName.ToLower();
                          
                            lock (_saveCertificateLocks.GetOrAdd(lockKey, new object()))
                            {
                                try
                                {
                             
                                    certificateCache.SaveCertificate(subjectName, certificate);
                                }
                                finally
                                {
                               
                                    _saveCertificateLocks.TryRemove(lockKey, out var _);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            OnException(new Exception("Failed to save fake certificate.", e));
                        }
                    });
                }
            }
            else
            {
                certificate = MakeCertificate(certificateName, isRootCertificate);
            }
        }
        catch (Exception e)
        {
            OnException(e);
            certificate = null;
        }

        return certificate;
    }

    public async Task<X509Certificate2?> CreateServerCertificate(string certificateName)
    {
        if (cachedCertificates.TryGetValue(certificateName, out var cached))
        {
            cached.LastAccess = DateTime.UtcNow;
            return cached.Certificate;
        }

        var createdTask = false;
        Task<X509Certificate2?> createCertificateTask;
        await pendingCertificateCreationTaskLock.WaitAsync();
        try
        {
            if (cachedCertificates.TryGetValue(certificateName, out cached))
            {
                cached.LastAccess = DateTime.UtcNow;
                return cached.Certificate;
            }

            if (!pendingCertificateCreationTasks.TryGetValue(certificateName, out createCertificateTask))
            {
                createCertificateTask = Task.Run(() =>
                {
                    var result = CreateCertificate(certificateName, false);
                    if (result != null) cachedCertificates.TryAdd(certificateName, new CachedCertificate(result));

                    return result;
                });

                pendingCertificateCreationTasks[certificateName] = createCertificateTask;
                createdTask = true;
            }
        }
        finally
        {
            pendingCertificateCreationTaskLock.Release();
        }

        var certificate = await createCertificateTask;

        if (createdTask)
        {
            await pendingCertificateCreationTaskLock.WaitAsync();
            try
            {
                pendingCertificateCreationTasks.Remove(certificateName);
            }
            finally
            {
                pendingCertificateCreationTaskLock.Release();
            }
        }

        return certificate;
    }

    internal async void ClearIdleCertificates()
    {
        var cancellationToken = clearCertificatesTokenSource.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            var cutOff = DateTime.UtcNow.AddMinutes(-CertificateCacheTimeOutMinutes);

            var outdated = cachedCertificates.Where(x => x.Value.LastAccess < cutOff).ToList();

            foreach (var cache in outdated) cachedCertificates.TryRemove(cache.Key, out _);
            try
            {
                await Task.Delay(1000 * 60, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    internal void StopClearIdleCertificates()
    {
        clearCertificatesTokenSource.Cancel();
    }

    public bool CreateRootCertificate(bool persistToFile = true)
    {
        lock (rootCertCreationLock)
        {
            if (persistToFile && RootCertificate == null) RootCertificate = LoadRootCertificate();

            if (RootCertificate != null) return true;

            if (!OverwritePfxFile)
                try
                {
                    var rootCert = certificateCache.LoadRootCertificate(PfxFilePath, PfxPassword,
                        X509KeyStorageFlags.Exportable);

                    if (rootCert != null && rootCert.NotAfter <= DateTime.Now)
                    {
                        OnException(new Exception("Loaded root certificate has expired."));
                        return false;
                    }

                    if (rootCert != null)
                    {
                        RootCertificate = rootCert;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    OnException(new Exception("Root cert cannot be loaded.", e));
                }

            try
            {
                RootCertificate = CreateCertificate(RootCertificateName, true);
            }
            catch (Exception e)
            {
                OnException(e);
            }

            if (persistToFile && RootCertificate != null)
                try
                {
                    try
                    {
                        certificateCache.Clear();
                    }
                    catch (Exception e)
                    {
                        OnException(new Exception("An error happened when clearing certificate cache.", e));
                    }

                    certificateCache.SaveRootCertificate(PfxFilePath, PfxPassword, RootCertificate);
                }
                catch (Exception e)
                {
                    OnException(e);
                }

            return RootCertificate != null;
        }
    }

    public X509Certificate2? LoadRootCertificate()
    {
        try
        {
            var rootCert = certificateCache.LoadRootCertificate(PfxFilePath, PfxPassword, X509KeyStorageFlags.Exportable);
            if (rootCert != null && rootCert.NotAfter <= DateTime.Now)
            {
                OnException(new ArgumentException("Loaded root certificate has expired."));
                return null;
            }
            return rootCert;
        }
        catch (Exception e)
        {
            OnException(e);
            return null;
        }
    }

    public bool LoadRootCertificate(string pfxFilePath, string password, bool overwritePfXFile = true,
        X509KeyStorageFlags storageFlag = X509KeyStorageFlags.Exportable)
    {
        PfxFilePath = pfxFilePath;
        PfxPassword = password;
        OverwritePfxFile = overwritePfXFile;
        StorageFlag = storageFlag;

        RootCertificate = LoadRootCertificate();

        return RootCertificate != null;
    }

    public void TrustRootCertificate(bool machineTrusted = false)
    {
        InstallCertificate(StoreName.My, StoreLocation.CurrentUser);
        if (!machineTrusted)
        {
            InstallCertificate(StoreName.Root, StoreLocation.CurrentUser);
        }
        else
        {
            InstallCertificate(StoreName.My, StoreLocation.LocalMachine);

            InstallCertificate(StoreName.Root, StoreLocation.LocalMachine);
        }
    }

    public bool TrustRootCertificateAsAdmin(bool machineTrusted = false)
    {
        if (!RunTime.IsWindows) return false;

        InstallCertificate(StoreName.My, StoreLocation.CurrentUser);

        var pfxFileName = Path.GetTempFileName();
        File.WriteAllBytes(pfxFileName, RootCertificate!.Export(X509ContentType.Pkcs12, PfxPassword));

        var info = new ProcessStartInfo
        {
            FileName = "certutil.exe",
            CreateNoWindow = true,
            UseShellExecute = true,
            Verb = "runas",
            ErrorDialog = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!machineTrusted)
            info.Arguments = "-f -user -p \"" + PfxPassword + "\" -importpfx root \"" + pfxFileName + "\"";
        else
            info.Arguments = "-importPFX -p \"" + PfxPassword + "\" -f \"" + pfxFileName + "\"";

        try
        {
            var process = Process.Start(info);
            if (process == null) return false;

            process.WaitForExit();
            File.Delete(pfxFileName);
        }
        catch (Exception e)
        {
            OnException(e);
            return false;
        }

        return true;
    }

    public void EnsureRootCertificate()
    {
        if (!CertValidated) CreateRootCertificate();

        if (TrustRootAsAdministrator)
            TrustRootCertificateAsAdmin(MachineTrustRoot);
        else if (UserTrustRoot) TrustRootCertificate(MachineTrustRoot);
    }

    public void EnsureRootCertificate(bool userTrustRootCertificate,
        bool machineTrustRootCertificate, bool trustRootCertificateAsAdmin = false)
    {
        UserTrustRoot = userTrustRootCertificate || machineTrustRootCertificate;
        MachineTrustRoot = machineTrustRootCertificate;
        TrustRootAsAdministrator = trustRootCertificateAsAdmin;

        EnsureRootCertificate();
    }

    public bool IsRootCertificateUserTrusted()
    {
        return RootCertificateInstalled(StoreLocation.CurrentUser) || IsRootCertificateMachineTrusted();
    }

    public bool IsRootCertificateMachineTrusted()
    {
        return RootCertificateInstalled(StoreLocation.LocalMachine);
    }

    public void RemoveTrustedRootCertificate(bool machineTrusted = false)
    {
        UninstallCertificate(StoreName.My, StoreLocation.CurrentUser, RootCertificate);

        if (!machineTrusted)
        {
            UninstallCertificate(StoreName.Root, StoreLocation.CurrentUser, RootCertificate);
        }
        else
        {
            UninstallCertificate(StoreName.My, StoreLocation.LocalMachine, RootCertificate);

            UninstallCertificate(StoreName.Root, StoreLocation.LocalMachine, RootCertificate);
        }
    }

    public bool RemoveTrustedRootCertificateAsAdmin(bool machineTrusted = false)
    {
        if (!RunTime.IsWindows) return false;

        UninstallCertificate(StoreName.My, StoreLocation.CurrentUser, RootCertificate);

        var infos = new List<ProcessStartInfo>();
        if (!machineTrusted)
            infos.Add(new ProcessStartInfo
            {
                FileName = "certutil.exe",
                Arguments = "-delstore -user Root \"" + RootCertificateName + "\"",
                CreateNoWindow = true,
                UseShellExecute = true,
                Verb = "runas",
                ErrorDialog = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        else
            infos.AddRange(
                new List<ProcessStartInfo>
                {
                    new()
                    {
                        FileName = "certutil.exe",
                        Arguments = "-delstore My \"" + RootCertificateName + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas",
                        ErrorDialog = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    },

                    new()
                    {
                        FileName = "certutil.exe",
                        Arguments = "-delstore Root \"" + RootCertificateName + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas",
                        ErrorDialog = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                });

        var success = true;
        try
        {
            foreach (var info in infos)
            {
                var process = Process.Start(info);

                if (process == null) success = false;

                process?.WaitForExit();
            }
        }
        catch
        {
            success = false;
        }

        return success;
    }

    public void ClearRootCertificate()
    {
        certificateCache.Clear();
        cachedCertificates.Clear();
        rootCertificate = null;
    }

    private void Dispose(bool disposing)
    {
        if (disposed) return;

        if (disposing) clearCertificatesTokenSource.Dispose();

        disposed = true;
    }

    ~CertificateManager()
    {
        Dispose(false);
    }
}