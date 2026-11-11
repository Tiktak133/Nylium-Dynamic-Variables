using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AdvancedDNV
{
    /// <summary>
    /// Plik .dnv: drzewo danych (<see cref="main"/>) i metadane, opcjonalnie z hasłem (AES), kompresją (zstd) i autozapisem.
    /// Układ pliku: [16B "DNVFile_byNylium"][2B wersja][4B długość metadanych][metadane (XOR)][main (zstd, potem AES)].
    /// Bezpieczny wątkowo w obrębie jednego obiektu; dwa obiekty DNV (albo dwa procesy) na tym samym pliku nie są koordynowane.
    /// </summary>
    public class DNV
    {
        private readonly string _filePath;
        private readonly string _password;
        private readonly Lock _fileLock = new(); // otwieranie, zapis, zamykanie

        private Container? _conMain;
        private Container? _metadata;

        // ── Autozapis: zapis po _autoSaveDelayMs ciszy od OSTATNIEJ zmiany, ale nie później niż _autoSaveMaxWaitMs
        //    od pierwszej niezapisanej zmiany (żeby ciągłe zmiany też w końcu trafiły na dysk).
        private readonly System.Threading.Timer? _autoSaveTimer; // Change() jest bezpieczne wątkowo (System.Timers.Timer nie był)
        private readonly int    _autoSaveDelayMs;
        private readonly int    _autoSaveMaxWaitMs;
        private readonly Lock _changeLock = new(); // _dirty, _firstChangeTicks i harmonogram timera
        private bool _dirty;                         // czy main zmienił się od ostatniego zapisu
        private long _firstChangeTicks = -1;         // Environment.TickCount64 pierwszej niezapisanej zmiany, -1 = brak

        // ── Klucz szyfrowania używany przez kolejne zapisy i czas ostatniej kopii .bak
        private FileKey? _key;
        private DateTime _lastBackupUtc = DateTime.MinValue;

        internal TimeSpan KeyLifetime     { get; set; } = DNVProperties.KeyLifetime;
        internal TimeSpan BackupInterval  { get; set; } = DNVProperties.BackupInterval;
        internal int      AutoSaveRetryMs { get; set; } = DNVProperties.AutoSaveRetryMs;

        public iMeta Meta { get; }
        public bool isOpened { get; private set; }

        /// <param name="AutoSaveDelayMs">Zapis po tylu ms bez kolejnych zmian; 0 lub mniej wyłącza autozapis</param>
        public DNV(string filePath, string Password, int AutoSaveDelayMs)
        {
            _filePath = filePath;
            _password = Password ?? string.Empty;
            Meta      = new iMeta(this);

            if (AutoSaveDelayMs > 0)
            {
                _autoSaveDelayMs   = AutoSaveDelayMs;
                _autoSaveMaxWaitMs = (int)Math.Clamp(AutoSaveDelayMs * 5L, 1000, int.MaxValue);
                _autoSaveTimer     = new System.Threading.Timer(_ => AutoSave());
            }
        }

        public DNV(string filePath, string Password, bool AutoSave) : this(filePath, Password, AutoSave ? DNVProperties.AutoSaveDelayMs : -1) { }
        public DNV(string filePath, string Password)                : this(filePath, Password, DNVProperties.AutoSaveEnable) { }
        public DNV(string filePath, int AutoSaveDelayMs)            : this(filePath, string.Empty, AutoSaveDelayMs) { }
        public DNV(string filePath, bool AutoSave)                  : this(filePath, string.Empty, AutoSave) { }
        public DNV(string filePath)                                 : this(filePath, string.Empty) { }

        /// <summary> Główny kontener z danymi pliku </summary>
        public Container main => isOpened && _conMain != null ? _conMain : throw new Exception("First open the DNV file");

        // ── Otwieranie ──────────────────────────────────────────────────────────────────────

        /// <summary> Wczytuje plik do pamięci (albo zaczyna pusty, jeśli pliku nie ma) </summary>
        /// <exception cref="CryptographicException">Złe hasło lub uszkodzone zaszyfrowane dane</exception>
        /// <exception cref="FormatException">Uszkodzona struktura danych</exception>
        public bool Open()
        {
            lock (_fileLock)
            {
                if (isOpened)
                    return true;

                _key           = null;
                _lastBackupUtc = DateTime.MinValue; // pierwszy zapis w sesji zrobi kopię stanu sprzed sesji

                if (File.Exists(_filePath))
                {
                    LoadFromFile();
                }
                else
                {
                    _conMain  = new Container("Main", null);
                    _metadata = new Container("MetaData", null);
                }

                lock (_changeLock)
                {
                    _dirty            = false;
                    _firstChangeTicks = -1;
                }

                _conMain!.ValueUpdated += OnMainChanged;
                isOpened = true;
                return true;
            }
        }

        private void LoadFromFile()
        {
            using var fs     = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            byte[] signature = reader.ReadBytes(16);
            if (Encoding.ASCII.GetString(signature) != DNVProperties.FileSignature)
            {
                LoadOlderFormat(reader.ReadBytes((int)(fs.Length - fs.Position)), signature);
                return;
            }

            ushort version = reader.ReadUInt16();
            if (version < DNVProperties.MyVersion)
                throw new Exception("DNV File is not supported. Using version is too old");

            byte[] metadataBytes = reader.ReadBytes(reader.ReadInt32());
            byte[] mainBytes     = reader.ReadBytes((int)(fs.Length - fs.Position));

            _metadata = ReadMetadata(metadataBytes);

            var    encryption = _metadata["encryption"];
            byte[] salt       = encryption.Value("salt").Get<byte[]>() ?? Array.Empty<byte>();
            if (salt.Length > 0 && _password.Length == 0)
                throw new Exception("Password is required");

            try
            {
                if (salt.Length > 0)
                    mainBytes = Decrypt(mainBytes, salt, encryption);
                if (_metadata["compression"].Value("enable").Get<bool>())
                    mainBytes = ZstdHelper.Decompress(mainBytes);

                _conMain = Container.Parse(mainBytes, 0, mainBytes.Length, "Main");
            }
            catch (Exception e) when (salt.Length > 0 && e is not CryptographicException)
            {
                // Złe hasło czasem przechodzi kontrolę dopełnienia AES i wychodzi dopiero przy rozpakowaniu lub parsowaniu.
                // Dla zaszyfrowanego pliku złe hasło i uszkodzenie są nie do odróżnienia — zawsze ten sam wyjątek.
                throw new CryptographicException("Incorrect DNV password (or damaged file).", e);
            }

            _metadata.Value("openCounter").Add(1);
        }

        private static Container ReadMetadata(byte[] bytes)
        {
            if (bytes.Length <= 4)
                return new Container("MetaData", null);

            DNVEncryption.SimpleEncryptInPlace(bytes, DNVProperties.MetadataEncryption);
            try
            {
                return Container.Parse(bytes, 0, bytes.Length, "MetaData");
            }
            catch (FormatException)
            {
                return new Container("MetaData", null); // uszkodzone metadane nie blokują odczytu danych
            }
        }

        private byte[] Decrypt(byte[] mainBytes, byte[] salt, Container encryption)
        {
            int    level        = encryption.Value("securityLevel").Get<int>();
            byte[] controlBlock = encryption.Value("controlBlock").Get<byte[]>() ?? Array.Empty<byte>();
            var (key, iv) = DNVEncryption.GenerateAESKeyFromPassword(_password, salt, level);

            if (!DNVEncryption.VerifyControlBlock(controlBlock, key, iv))
                throw new CryptographicException("Incorrect DNV password (or damaged file).");

            // Kolejne zapisy użyją tego klucza, dopóki nie minie KeyLifetime od jego utworzenia.
            // Pliki sprzed tej zmiany nie mają "keyCreated" — wtedy klucz zmieni się przy pierwszym zapisie.
            _key = new FileKey(salt, level, key, iv, controlBlock, encryption.Value("keyCreated").Get(DateTime.MinValue));
            return DNVEncryption.DecryptData(mainBytes, key, iv);
        }

        /// <summary>
        /// Format sprzed AdvancedDNV. Gdy nie da się go odczytać, plik otwiera się pusty (tak jak dotychczas) —
        /// launcher startuje wtedy z czystymi ustawieniami zamiast się wywracać.
        /// </summary>
        private void LoadOlderFormat(byte[] content, byte[] signature)
        {
            var main = new Container("Main", null);
            try
            {
                OlderDNV.ReadOlderDNV(content, main, signature);
            }
            catch
            {
                main = new Container("Main", null);
            }
            _conMain  = main;
            _metadata = new Container("MetaData", null);
        }

        // ── Zapis ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Zapisuje stan do pliku, jeśli coś zmieniło się od ostatniego zapisu (albo pliku jeszcze nie ma).
        /// Zapis idzie przez plik tymczasowy, więc przerwany zapis nie psuje pliku.
        /// </summary>
        public void Save()
        {
            lock (_fileLock)
            {
                if (!isOpened || _conMain == null || _metadata == null)
                    return;

                bool fileExists = File.Exists(_filePath);
                lock (_changeLock)
                {
                    if (!_dirty && fileExists)
                        return; // nic się nie zmieniło — plik jest aktualny

                    // Zerujemy PRZED zapisem: zmiana w trakcie zapisu ustawi _dirty ponownie i zaplanuje kolejny zapis
                    _dirty            = false;
                    _firstChangeTicks = -1;
                    _autoSaveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }

                try
                {
                    WriteCurrentState();
                }
                catch
                {
                    lock (_changeLock)
                        _dirty = true; // zmiany nadal czekają na zapis
                    throw;
                }
            }
        }

        public void SaveAndClose()
        {
            Save();
            Close();
        }

        private void WriteCurrentState()
        {
            _metadata!.Value("saveCounter").Add(1);
            UpdateAuthorship(_metadata["user"]);

            byte[] main = _conMain!.CompileContainer();
            var compression = _metadata["compression"];
            compression.Value("initialSize").Set(main.Length);

            if (main.Length > DNVProperties.MinimumCompressionByteCount)
            {
                compression.Value("enable").Set(true);
                compression.Value("compressionLevel").Set(DNVProperties.CompressionLevel);
                main = ZstdHelper.Compress(main, DNVProperties.CompressionLevel);
            }
            else
            {
                compression.Value("enable").Set(false);
                compression.Value("compressionLevel").Drop();
            }

            main = EncryptIfPasswordSet(main);
            compression.Value("finalSize").Set(main.Length);

            byte[] metadata = _metadata.CompileContainer();
            DNVEncryption.SimpleEncryptInPlace(metadata, DNVProperties.MetadataEncryption);

            if (main.Length + metadata.Length + 4096L >= int.MaxValue)
                throw new Exception("DNV is too long");

            WriteFileAtomically(metadata, main);
        }

        private static void UpdateAuthorship(Container user)
        {
            string fullUser = Environment.UserDomainName + "\\" + Environment.UserName;
            if (!user.Value("author").IsSet())
            {
                user.Value("author").Set(fullUser);
                user.Value("created").Set(DateTime.UtcNow);
            }
            user.Value("editor").Set(fullUser);
            user.Value("edited").Set(DateTime.UtcNow);
        }

        private byte[] EncryptIfPasswordSet(byte[] main)
        {
            var encryption = _metadata!["encryption"];
            if (_password.Length == 0)
            {
                encryption.DropContainer();
                return main;
            }

            // Nowa sól i klucz najwyżej raz na KeyLifetime — PBKDF2 przy każdym zapisie kosztował ~3 ms (więcej na telefonie)
            if (_key == null || DateTime.UtcNow - _key.CreatedUtc >= KeyLifetime)
            {
                const int level = (int)DNVEncryption.SecurityLevel.Optimal;
                byte[] salt = DNVEncryption.RandomSalt();
                var (key, iv) = DNVEncryption.GenerateAESKeyFromPassword(_password, salt, level);
                _key = new FileKey(salt, level, key, iv, DNVEncryption.GenerateControlBlock(key, iv), DateTime.UtcNow);
            }

            encryption.Value("salt").Set(_key.Salt);
            encryption.Value("securityLevel").Set(_key.Level);
            encryption.Value("controlBlock").Set(_key.ControlBlock);
            encryption.Value("keyCreated").Set(_key.CreatedUtc);

            return DNVEncryption.EncryptData(main, _key.Key, _key.Iv);
        }

        private void WriteFileAtomically(byte[] metadata, byte[] main)
        {
            string tempFilePath = _filePath + "tmp";
            long expectedSize;

            using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                Span<byte> header = stackalloc byte[16 + 2 + 4];
                Encoding.ASCII.GetBytes(DNVProperties.FileSignature, header);
                BinaryPrimitives.WriteUInt16LittleEndian(header[16..], DNVProperties.MyVersion);
                BinaryPrimitives.WriteInt32LittleEndian(header[18..], metadata.Length);

                fs.Write(header);
                fs.Write(metadata);
                fs.Write(main);
                fs.Flush(flushToDisk: true);
                expectedSize = fs.Position;
            }

            long actualSize = new FileInfo(tempFilePath).Length;
            if (actualSize != expectedSize)
                throw new Exception($"Zapisany plik {tempFilePath} ma nieoczekiwany rozmiar ({actualSize}B z {expectedSize}B) – zapis uszkodzony, akcja przerwana.");

            if (!File.Exists(_filePath))
            {
                File.Move(tempFilePath, _filePath);
                return;
            }

            // Poprzednia wersja trafia do .bak przy pierwszym zapisie w sesji, potem nie częściej niż co BackupInterval —
            // kopia jest wtedy stanem sprzed kilku minut, a nie sprzed ułamka sekundy.
            bool makeBackup = DateTime.UtcNow - _lastBackupUtc >= BackupInterval;
            File.Replace(tempFilePath, _filePath, makeBackup ? _filePath + ".bak" : null);
            if (makeBackup)
                _lastBackupUtc = DateTime.UtcNow;
        }

        // ── Autozapis i zamykanie ───────────────────────────────────────────────────────────

        private void OnMainChanged()
        {
            lock (_changeLock)
            {
                _dirty = true;
                if (_autoSaveTimer == null)
                    return;

                long now = Environment.TickCount64;
                if (_firstChangeTicks < 0)
                    _firstChangeTicks = now;

                long dueIn = Math.Min(_autoSaveDelayMs, _firstChangeTicks + _autoSaveMaxWaitMs - now);
                _autoSaveTimer.Change(Math.Max(0, dueIn), Timeout.Infinite);
            }
        }

        private void AutoSave()
        {
            try
            {
                Save();
            }
            catch
            {
                // Np. plik chwilowo zablokowany przez antywirusa. Wcześniej wyjątek leciał z async void i zabijał
                // cały proces — teraz ponawiamy po chwili, dopóki zmiany czekają na zapis.
                lock (_changeLock)
                    if (_dirty)
                        _autoSaveTimer?.Change(AutoSaveRetryMs, Timeout.Infinite);
            }
        }

        /// <summary> Zamyka plik BEZ zapisu (niezapisane zmiany przepadają — do zapisu służy SaveAndClose) </summary>
        public void Close()
        {
            lock (_fileLock)
            {
                if (!isOpened)
                    return;

                lock (_changeLock)
                {
                    _autoSaveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _dirty            = false;
                    _firstChangeTicks = -1;
                }

                _conMain!.ValueUpdated -= OnMainChanged;
                _conMain  = null;
                _metadata = null;
                _key      = null;
                isOpened  = false;
            }
        }

        /// <summary> Klucz pliku: sól, poziom, wyprowadzony klucz/IV, blok kontrolny hasła i chwila utworzenia </summary>
        private sealed record FileKey(byte[] Salt, int Level, byte[] Key, byte[] Iv, byte[] ControlBlock, DateTime CreatedUtc);

        // ── Metadane ────────────────────────────────────────────────────────────────────────

        public class iMeta
        {
            private readonly DNV _dnv;
            internal iMeta(DNV dnv) => _dnv = dnv;

            /// <summary> Wartość z metadanych: <c>section/name</c> albo sama <c>section</c>, gdy name == null </summary>
            private T Read<T>(string section, string? name, T defaultValue)
            {
                Container metadata = _dnv.isOpened && _dnv._metadata != null
                    ? _dnv._metadata
                    : throw new Exception("First open the DNV file");

                return name == null
                    ? metadata.Value(section).Get(defaultValue)
                    : metadata[section].Value(name).Get(defaultValue);
            }

            public bool     isCompressed       => Read("compression", "enable", false);
            public long     initialDataSize    => Read("compression", "initialSize", compressedDataSize);
            public long     compressedDataSize => Read("compression", "finalSize", 0L);

            public long     numberOfOpening    => Read("openCounter", null, 0L);
            public long     numberOfSaving     => Read("saveCounter", null, 0L);

            public string   author             => Read("user", "author", "N/A");
            public string   editor             => Read("user", "editor", "N/A");
            public DateTime created            => Read("user", "created", DateTime.MinValue);
            public DateTime edited             => Read("user", "edited", DateTime.MinValue);

            public bool     isEncrypted        => Read("encryption", "salt", Array.Empty<byte>()).Length > 0;
            public int      encryptionLevel    => Read("encryption", "securityLevel", 0);
        }
    }
}
