#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Telegram.Bot;
using DotNetEnv;

namespace MonitorFiel
{
    class Program
    {
        // ================================================================
        // URLs / CONFIGURAÇÃO
        // ================================================================

        private static string MATCH_URL;
        private static string CATEGORIA_URL;

        private static string TELEGRAM_BOT_TOKEN;
        private static string TELEGRAM_CHAT_ID;

        private static readonly string COOKIE_FILE =
            "session_cookies.json";


        // ================================================================
        // VELOCIDADE DO MONITOR
        // ================================================================

        private const int REFRESH_MIN_MS = 1000;
        private const int REFRESH_MAX_MS = 1800;

        private const int DOM_WAIT_MS = 1800;

        private const int UNKNOWN_STATE_RETRY_MS = 300;

        private const int RETRY_MS = 500;

        private const int ERROR_WAIT_MS = 2500;

        private const int SECTORS_UNLOCK_WAIT_MS = 4000;


        // ================================================================
        // ALERTA SONORO
        // ================================================================

        /*
         * Para testar a sirene imediatamente ao iniciar:
         *
         * true = testa
         * false = comportamento normal
         */
        private const bool TEST_SOUND_ON_START = false;


        /*
         * Enquanto houver ingresso:
         *
         * 6 segundos de sirene
         * 2 segundos de silêncio
         * 6 segundos de sirene
         * 2 segundos de silêncio
         * ...
         */
        private const int ALERT_SOUND_ON_MS = 6000;

        private const int ALERT_SOUND_PAUSE_MS = 2000;


        private static readonly object alarmLock =
            new object();


        private static CancellationTokenSource availabilityAlarmCts =
            null;


        // ================================================================
        // WINDOWS AUDIO
        // ================================================================

        private const uint SND_ASYNC = 0x0001;

        private const uint SND_NODEFAULT = 0x0002;

        private const uint SND_LOOP = 0x0008;

        private const uint SND_FILENAME = 0x00020000;


        private static readonly string ALERT_SOUND_FILE =
            Path.Combine(
                Path.GetTempPath(),
                "monitorfiel_sirene.wav"
            );


        [DllImport(
            "winmm.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true
        )]
        private static extern bool PlaySound(
            string pszSound,
            IntPtr hmod,
            uint fdwSound
        );


        private static readonly Random rnd =
            new Random();


        // ================================================================
        // ESTADOS
        // ================================================================

        private enum MonitorState
        {
            WaitingCategory,
            MonitoringSectors
        }


        // ================================================================
        // MAIN
        // ================================================================

        static async Task Main(string[] args)
        {
            Env.Load();


            // ============================================================
            // .ENV
            // ============================================================

            MATCH_URL =
                Environment.GetEnvironmentVariable(
                    "MATCH_URL"
                );


            TELEGRAM_BOT_TOKEN =
                Environment.GetEnvironmentVariable(
                    "TELEGRAM_BOT_TOKEN"
                );


            TELEGRAM_CHAT_ID =
                Environment.GetEnvironmentVariable(
                    "TELEGRAM_CHAT_ID"
                );


            if (
                string.IsNullOrWhiteSpace(MATCH_URL)
                ||
                string.IsNullOrWhiteSpace(TELEGRAM_BOT_TOKEN)
                ||
                string.IsNullOrWhiteSpace(TELEGRAM_CHAT_ID)
            )
            {
                Console.WriteLine(
                    "ERRO CRÍTICO: verifique MATCH_URL, " +
                    "TELEGRAM_BOT_TOKEN e TELEGRAM_CHAT_ID " +
                    "no arquivo .env."
                );

                return;
            }


            // ============================================================
            // MONTA /categoria/
            // ============================================================

            CATEGORIA_URL =
                BuildCategoryUrl(
                    MATCH_URL
                );


            Console.WriteLine();
            Console.WriteLine(
                "=================================================="
            );

            Console.WriteLine(
                "          MONITOR FIEL TORCEDOR - V29"
            );

            Console.WriteLine(
                "=================================================="
            );

            Console.WriteLine(
                $"Categoria : {CATEGORIA_URL}"
            );

            Console.WriteLine(
                $"Setores   : {MATCH_URL}"
            );

            Console.WriteLine();


            // ============================================================
            // PREPARA A SIRENE
            // ============================================================

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    CreateAlertSoundFile();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        "⚠️ Não foi possível preparar a sirene: "
                        +
                        ex.Message
                    );
                }
            }


            // ============================================================
            // TESTE OPCIONAL DO SOM
            // ============================================================

            if (TEST_SOUND_ON_START)
            {
                Console.WriteLine(
                    "🔊 TESTE: iniciando sirene..."
                );

                EnsureAvailabilityAlarmRunning();


                /*
                 * Dá tempo de ouvirmos:
                 *
                 * ~6s som
                 * ~2s pausa
                 * começo do próximo ciclo
                 */
                await Task.Delay(
                    8500
                );


                StopAvailabilityAlarm();


                await Task.Delay(
                    500
                );


                Console.WriteLine(
                    "🔊 Teste finalizado."
                );

                Console.WriteLine();
            }


            // ============================================================
            // CHROME
            // ============================================================

            var options =
                new ChromeOptions();


            options.AddArgument(
                "--start-maximized"
            );


            /*
             * Não espera imagens/fontes/etc.
             */
            options.PageLoadStrategy =
                PageLoadStrategy.Eager;


            string profilePath =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "chrome-profile"
                );


            Directory.CreateDirectory(
                profilePath
            );


            options.AddArgument(
                $"--user-data-dir={profilePath}"
            );


            IWebDriver driver =
                new ChromeDriver(
                    options
                );


            // ============================================================
            // CTRL+C
            // ============================================================

            Console.CancelKeyPress +=
                (sender, e) =>
                {
                    e.Cancel = true;


                    Console.WriteLine();
                    Console.WriteLine(
                        "Encerrando monitor..."
                    );


                    StopAvailabilityAlarm();

                    StopAlertSound();


                    try
                    {
                        driver.Quit();
                    }
                    catch
                    {
                    }


                    Environment.Exit(
                        0
                    );
                };


            try
            {
                // ========================================================
                // LOGIN
                // ========================================================

                bool loggedIn =
                    LoginRoutine(
                        driver
                    );


                if (!loggedIn)
                {
                    Console.WriteLine(
                        "Não foi possível iniciar a sessão."
                    );

                    return;
                }


                var botClient =
                    new TelegramBotClient(
                        TELEGRAM_BOT_TOKEN
                    );


                // ========================================================
                // ESTADO INICIAL
                // ========================================================

                MonitorState state =
                    MonitorState.WaitingCategory;


                /*
                 * Serve somente para evitar Telegram repetido.
                 *
                 * A sirene NÃO depende mais disso.
                 */
                string lastAvailability =
                    "";


                // ========================================================
                // ABRE CATEGORIA
                // ========================================================

                Console.WriteLine();
                Console.WriteLine(
                    "Abrindo /categoria/..."
                );


                NavigateToCategory(
                    driver
                );


                // ========================================================
                // LOOP PRINCIPAL
                // ========================================================

                while (true)
                {
                    try
                    {
                        // =================================================
                        // ESTADO 1
                        // ESPERANDO COMPRAR
                        // =================================================

                        if (
                            state ==
                            MonitorState.WaitingCategory
                        )
                        {
                            // =============================================
                            // LOGIN EXPIROU
                            // =============================================

                            if (
                                IsAuthPage(
                                    driver
                                )
                            )
                            {
                                StopAvailabilityAlarm();


                                Console.WriteLine();
                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "⚠️ Sessão expirada."
                                );


                                bool loginOk =
                                    LoginRoutine(
                                        driver
                                    );


                                if (!loginOk)
                                {
                                    Thread.Sleep(
                                        ERROR_WAIT_MS
                                    );

                                    continue;
                                }


                                NavigateToCategory(
                                    driver
                                );


                                continue;
                            }


                            // =============================================
                            // GARANTE /categoria/
                            // =============================================

                            if (
                                !IsCategoryPage(
                                    driver
                                )
                            )
                            {
                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "Fora de /categoria/. Voltando..."
                                );


                                NavigateToCategory(
                                    driver
                                );


                                continue;
                            }


                            // =============================================
                            // PROCURA COMPRAR
                            // =============================================

                            IWebElement comprarButton =
                                FindComprarButton(
                                    driver
                                );


                            if (
                                comprarButton != null
                            )
                            {
                                Console.WriteLine();
                                Console.WriteLine(
                                    "=================================================="
                                );

                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "🔥 COMPRAR APARECEU!"
                                );

                                Console.WriteLine(
                                    "Clicando imediatamente..."
                                );

                                Console.WriteLine(
                                    "=================================================="
                                );


                                bool clicked =
                                    ClickComprar(
                                        driver,
                                        comprarButton
                                    );


                                if (!clicked)
                                {
                                    Console.WriteLine(
                                        "Falha ao clicar em COMPRAR."
                                    );


                                    Thread.Sleep(
                                        RETRY_MS
                                    );


                                    continue;
                                }


                                // =========================================
                                // ESPERA /setores/
                                // =========================================

                                bool entrouSetores =
                                    WaitUntilSectorsUnlocked(
                                        driver,
                                        SECTORS_UNLOCK_WAIT_MS
                                    );


                                if (
                                    entrouSetores
                                )
                                {
                                    Console.WriteLine();
                                    Console.WriteLine(
                                        "=================================================="
                                    );

                                    Console.WriteLine(
                                        "✅ ACESSO A /SETORES/ LIBERADO"
                                    );

                                    Console.WriteLine(
                                        "O monitor NÃO voltará para /categoria/."
                                    );

                                    Console.WriteLine(
                                        "Agora monitorando NORTE e SUL."
                                    );

                                    Console.WriteLine(
                                        "=================================================="
                                    );

                                    Console.WriteLine();


                                    state =
                                        MonitorState.MonitoringSectors;


                                    lastAvailability =
                                        "";


                                    /*
                                     * Continua imediatamente para
                                     * verificar Norte/Sul.
                                     */
                                    continue;
                                }


                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "COMPRAR foi clicado, mas /setores/ " +
                                    "não abriu."
                                );


                                Thread.Sleep(
                                    RETRY_MS
                                );


                                continue;
                            }


                            // =============================================
                            // ESGOTADO
                            // =============================================

                            bool esgotado =
                                IsCategorySoldOut(
                                    driver
                                );


                            if (esgotado)
                            {
                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "Categoria: ❌ ESGOTADO"
                                );


                                WaitRandomRefresh();


                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "Atualizando /categoria/..."
                                );


                                driver.Navigate()
                                    .Refresh();


                                WaitForCategoryPage(
                                    driver,
                                    DOM_WAIT_MS
                                );


                                continue;
                            }


                            // =============================================
                            // DOM AINDA CARREGANDO
                            // =============================================

                            Console.WriteLine(
                                $"[{DateTime.Now:HH:mm:ss}] " +
                                "Categoria ainda carregando..."
                            );


                            Thread.Sleep(
                                UNKNOWN_STATE_RETRY_MS
                            );


                            WaitForCategoryPage(
                                driver,
                                DOM_WAIT_MS
                            );


                            continue;
                        }


                        // =================================================
                        // ESTADO 2
                        // MONITORANDO /setores/
                        // =================================================

                        if (
                            state ==
                            MonitorState.MonitoringSectors
                        )
                        {
                            // =============================================
                            // SESSÃO EXPIROU
                            // =============================================

                            if (
                                IsAuthPage(
                                    driver
                                )
                            )
                            {
                                StopAvailabilityAlarm();


                                Console.WriteLine();
                                Console.WriteLine(
                                    "=================================================="
                                );

                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "⚠️ SESSÃO EXPIROU"
                                );

                                Console.WriteLine(
                                    "Acesso a /setores/ perdido."
                                );

                                Console.WriteLine(
                                    "Voltando ao modo /categoria/."
                                );

                                Console.WriteLine(
                                    "=================================================="
                                );


                                state =
                                    MonitorState.WaitingCategory;


                                lastAvailability =
                                    "";


                                bool loginOk =
                                    LoginRoutine(
                                        driver
                                    );


                                if (!loginOk)
                                {
                                    Thread.Sleep(
                                        ERROR_WAIT_MS
                                    );

                                    continue;
                                }


                                NavigateToCategory(
                                    driver
                                );


                                continue;
                            }


                            // =============================================
                            // PERDEU /setores/
                            // =============================================

                            if (
                                !IsSectorsPage(
                                    driver
                                )
                            )
                            {
                                StopAvailabilityAlarm();


                                Console.WriteLine();
                                Console.WriteLine(
                                    "=================================================="
                                );

                                Console.WriteLine(
                                    $"[{DateTime.Now:HH:mm:ss}] " +
                                    "⚠️ ACESSO A /SETORES/ PERDIDO"
                                );

                                Console.WriteLine(
                                    $"URL atual: {driver.Url}"
                                );

                                Console.WriteLine(
                                    "Voltando para /categoria/."
                                );

                                Console.WriteLine(
                                    "=================================================="
                                );


                                state =
                                    MonitorState.WaitingCategory;


                                lastAvailability =
                                    "";


                                NavigateToCategory(
                                    driver
                                );


                                continue;
                            }


                            // =============================================
                            // VERIFICA NORTE
                            // =============================================

                            bool norteDisponivel =
                                CheckSectorAvailability(
                                    driver,
                                    "norte"
                                );


                            // =============================================
                            // VERIFICA SUL
                            // =============================================

                            bool sulDisponivel =
                                CheckSectorAvailability(
                                    driver,
                                    "sul"
                                );


                            Console.WriteLine(
                                $"[{DateTime.Now:HH:mm:ss}] " +
                                $"NORTE: " +
                                $"{(norteDisponivel ? "✅ DISPONÍVEL" : "❌")} " +
                                "| " +
                                $"SUL: " +
                                $"{(sulDisponivel ? "✅ DISPONÍVEL" : "❌")}"
                            );


                            // =============================================
                            // ESTADO ATUAL
                            // =============================================

                            bool temIngressoAlvo =
                                norteDisponivel
                                ||
                                sulDisponivel;


                            string availabilityKey =
                                $"{norteDisponivel}-{sulDisponivel}";


                            // =============================================
                            // SIRENE CONTÍNUA
                            // =============================================

                            if (
                                temIngressoAlvo
                            )
                            {
                                /*
                                 * Se já estiver ativa:
                                 * não cria outra.
                                 *
                                 * Se não estiver:
                                 * começa a sirene.
                                 */
                                EnsureAvailabilityAlarmRunning();
                            }
                            else
                            {
                                /*
                                 * Norte e Sul indisponíveis.
                                 *
                                 * Para imediatamente.
                                 */
                                StopAvailabilityAlarm();
                            }


                            // =============================================
                            // TELEGRAM
                            // =============================================

                            /*
                             * Telegram continua sendo enviado somente
                             * quando a disponibilidade surge ou muda.
                             *
                             * Isso evita dezenas de mensagens.
                             *
                             * A SIRENE NÃO depende desta condição.
                             */
                            if (
                                temIngressoAlvo
                                &&
                                availabilityKey
                                != lastAvailability
                            )
                            {
                                Console.WriteLine();
                                Console.WriteLine(
                                    "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
                                );

                                Console.WriteLine(
                                    "🚨🚨🚨 INGRESSO ENCONTRADO 🚨🚨🚨"
                                );

                                Console.WriteLine(
                                    "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
                                );

                                Console.WriteLine();


                                string msg =
                                    "🚨 ALERTA FIEL!\n\n";


                                if (
                                    norteDisponivel
                                )
                                {
                                    msg +=
                                        "✅ SETOR NORTE DISPONÍVEL\n";
                                }


                                if (
                                    sulDisponivel
                                )
                                {
                                    msg +=
                                        "✅ SETOR SUL DISPONÍVEL\n";
                                }


                                msg +=
                                    "\nCORRA PARA O FIEL TORCEDOR!\n\n";


                                msg +=
                                    MATCH_URL;


                                Console.WriteLine(
                                    "Enviando alerta pelo Telegram..."
                                );


                                try
                                {
                                    await botClient.SendMessage(
                                        chatId:
                                            TELEGRAM_CHAT_ID,

                                        text:
                                            msg
                                    );


                                    Console.WriteLine(
                                        "✅ Telegram enviado."
                                    );
                                }
                                catch (
                                    Exception telegramEx
                                )
                                {
                                    Console.WriteLine(
                                        "Erro ao enviar Telegram: "
                                        +
                                        telegramEx.Message
                                    );
                                }
                            }


                            // =============================================
                            // MEMORIZA ESTADO PARA O TELEGRAM
                            // =============================================

                            lastAvailability =
                                availabilityKey;


                            // =============================================
                            // ESPERA
                            // =============================================

                            WaitRandomRefresh();


                            // =============================================
                            // REFRESH SOMENTE DE /setores/
                            // =============================================

                            Console.WriteLine(
                                $"[{DateTime.Now:HH:mm:ss}] " +
                                "Atualizando /setores/..."
                            );


                            driver.Navigate()
                                .Refresh();


                            WaitForSectorsPage(
                                driver,
                                DOM_WAIT_MS
                            );


                            continue;
                        }
                    }
                    catch (
                        WebDriverException ex
                    )
                    {
                        Console.WriteLine(
                            $"WebDriver erro: {ex.Message}"
                        );


                        Thread.Sleep(
                            ERROR_WAIT_MS
                        );
                    }
                    catch (
                        Exception ex
                    )
                    {
                        Console.WriteLine(
                            $"Erro no loop: {ex.Message}"
                        );


                        Thread.Sleep(
                            ERROR_WAIT_MS
                        );
                    }
                }
            }
            finally
            {
                StopAvailabilityAlarm();

                StopAlertSound();


                try
                {
                    driver.Quit();
                }
                catch
                {
                }
            }
        }


        // ================================================================
        // GERA WAV DA SIRENE
        // ================================================================

        private static void CreateAlertSoundFile()
        {
            const int sampleRate = 44100;

            const short bitsPerSample = 16;

            const short channels = 1;

            const double durationSeconds = 1.0;


            int totalSamples =
                (int)(
                    sampleRate
                    *
                    durationSeconds
                );


            int bytesPerSample =
                bitsPerSample / 8;


            int dataSize =
                totalSamples
                *
                channels
                *
                bytesPerSample;


            using FileStream fs =
                new FileStream(
                    ALERT_SOUND_FILE,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read
                );


            using BinaryWriter writer =
                new BinaryWriter(
                    fs
                );


            // ============================================================
            // RIFF
            // ============================================================

            writer.Write(
                new char[]
                {
                    'R',
                    'I',
                    'F',
                    'F'
                }
            );


            writer.Write(
                36
                +
                dataSize
            );


            writer.Write(
                new char[]
                {
                    'W',
                    'A',
                    'V',
                    'E'
                }
            );


            // ============================================================
            // FMT
            // ============================================================

            writer.Write(
                new char[]
                {
                    'f',
                    'm',
                    't',
                    ' '
                }
            );


            writer.Write(
                16
            );


            // PCM
            writer.Write(
                (short)1
            );


            writer.Write(
                channels
            );


            writer.Write(
                sampleRate
            );


            int byteRate =
                sampleRate
                *
                channels
                *
                bytesPerSample;


            writer.Write(
                byteRate
            );


            short blockAlign =
                (short)(
                    channels
                    *
                    bytesPerSample
                );


            writer.Write(
                blockAlign
            );


            writer.Write(
                bitsPerSample
            );


            // ============================================================
            // DATA
            // ============================================================

            writer.Write(
                new char[]
                {
                    'd',
                    'a',
                    't',
                    'a'
                }
            );


            writer.Write(
                dataSize
            );


            // ============================================================
            // GERA O SOM
            // ============================================================

            double phase =
                0.0;


            /*
             * Frequências escolhidas para serem
             * difíceis de ignorar.
             */
            const double minFrequency =
                750.0;


            const double maxFrequency =
                2200.0;


            /*
             * PCM 16-bit vai aproximadamente até 32767.
             */
            const double amplitude =
                31000.0;


            for (
                int i = 0;
                i < totalSamples;
                i++
            )
            {
                double time =
                    (double)i
                    /
                    sampleRate;


                double position =
                    time
                    %
                    1.0;


                double frequency;


                /*
                 * Primeira metade:
                 * frequência sobe.
                 */
                if (
                    position < 0.5
                )
                {
                    frequency =
                        minFrequency
                        +
                        (
                            maxFrequency
                            -
                            minFrequency
                        )
                        *
                        (
                            position
                            /
                            0.5
                        );
                }

                /*
                 * Segunda metade:
                 * frequência desce.
                 */
                else
                {
                    frequency =
                        maxFrequency
                        -
                        (
                            maxFrequency
                            -
                            minFrequency
                        )
                        *
                        (
                            (
                                position
                                -
                                0.5
                            )
                            /
                            0.5
                        );
                }


                phase +=
                    2.0
                    *
                    Math.PI
                    *
                    frequency
                    /
                    sampleRate;


                /*
                 * Fundamental + harmônicos.
                 */
                double signal =
                    Math.Sin(
                        phase
                    )
                    +
                    0.30
                    *
                    Math.Sin(
                        phase * 2.0
                    )
                    +
                    0.15
                    *
                    Math.Sin(
                        phase * 3.0
                    );


                signal /=
                    1.45;


                short sample =
                    (short)(
                        signal
                        *
                        amplitude
                    );


                writer.Write(
                    sample
                );
            }
        }


        // ================================================================
        // GARANTE QUE A SIRENE CONTÍNUA ESTÁ RODANDO
        // ================================================================

        private static void EnsureAvailabilityAlarmRunning()
        {
            lock (
                alarmLock
            )
            {
                /*
                 * Já existe um worker ativo.
                 */
                if (
                    availabilityAlarmCts != null
                    &&
                    !availabilityAlarmCts.IsCancellationRequested
                )
                {
                    return;
                }


                availabilityAlarmCts =
                    new CancellationTokenSource();


                CancellationTokenSource localCts =
                    availabilityAlarmCts;


                CancellationToken token =
                    localCts.Token;


                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            if (
                                !OperatingSystem.IsWindows()
                            )
                            {
                                Console.WriteLine(
                                    "⚠️ Sirene disponível somente no Windows."
                                );

                                return;
                            }


                            /*
                             * Se por algum motivo o WAV sumiu,
                             * recria.
                             */
                            if (
                                !File.Exists(
                                    ALERT_SOUND_FILE
                                )
                            )
                            {
                                CreateAlertSoundFile();
                            }


                            Console.WriteLine();
                            Console.WriteLine(
                                "🔊🔊🔊 SIRENE CONTÍNUA ATIVADA! 🔊🔊🔊"
                            );

                            Console.WriteLine(
                                $"🔊 {ALERT_SOUND_ON_MS / 1000}s tocando / " +
                                $"{ALERT_SOUND_PAUSE_MS / 1000}s de pausa."
                            );


                            // ============================================
                            // LOOP DA SIRENE
                            // ============================================

                            while (
                                !token.IsCancellationRequested
                            )
                            {
                                // ========================================
                                // COMEÇA A TOCAR
                                // ========================================

                                bool started =
                                    PlaySound(
                                        ALERT_SOUND_FILE,
                                        IntPtr.Zero,
                                        SND_FILENAME
                                        |
                                        SND_ASYNC
                                        |
                                        SND_LOOP
                                        |
                                        SND_NODEFAULT
                                    );


                                if (!started)
                                {
                                    int error =
                                        Marshal.GetLastWin32Error();


                                    Console.WriteLine(
                                        "⚠️ Windows não conseguiu iniciar " +
                                        $"a sirene. Código: {error}"
                                    );


                                    break;
                                }


                                // ========================================
                                // FICA TOCANDO
                                // ========================================

                                try
                                {
                                    await Task.Delay(
                                        ALERT_SOUND_ON_MS,
                                        token
                                    );
                                }
                                catch (
                                    OperationCanceledException
                                )
                                {
                                    break;
                                }


                                // ========================================
                                // SILÊNCIO
                                // ========================================

                                StopAlertSound();


                                if (
                                    token.IsCancellationRequested
                                )
                                {
                                    break;
                                }


                                Console.WriteLine(
                                    "🔇 Pausa da sirene..."
                                );


                                try
                                {
                                    await Task.Delay(
                                        ALERT_SOUND_PAUSE_MS,
                                        token
                                    );
                                }
                                catch (
                                    OperationCanceledException
                                )
                                {
                                    break;
                                }


                                if (
                                    !token.IsCancellationRequested
                                )
                                {
                                    Console.WriteLine(
                                        "🔊 Sirene novamente..."
                                    );
                                }
                            }
                        }
                        catch (
                            OperationCanceledException
                        )
                        {
                        }
                        catch (
                            Exception ex
                        )
                        {
                            Console.WriteLine(
                                "Erro na sirene contínua: "
                                +
                                ex.Message
                            );
                        }
                        finally
                        {
                            /*
                             * IMPORTANTE:
                             *
                             * Só esta task pode parar o som
                             * se ainda for a instância atual.
                             *
                             * Evita uma task antiga desligar
                             * uma sirene nova.
                             */
                            bool ownsCurrentAlarm;


                            lock (
                                alarmLock
                            )
                            {
                                ownsCurrentAlarm =
                                    ReferenceEquals(
                                        availabilityAlarmCts,
                                        localCts
                                    );


                                if (
                                    ownsCurrentAlarm
                                )
                                {
                                    availabilityAlarmCts =
                                        null;
                                }
                            }


                            if (
                                ownsCurrentAlarm
                            )
                            {
                                StopAlertSound();
                            }


                            localCts.Dispose();
                        }
                    }
                );
            }
        }


        // ================================================================
        // PARA A SIRENE CONTÍNUA
        // ================================================================

        private static void StopAvailabilityAlarm()
        {
            CancellationTokenSource cts;


            lock (
                alarmLock
            )
            {
                /*
                 * Não existe sirene ativa.
                 */
                if (
                    availabilityAlarmCts == null
                )
                {
                    return;
                }


                cts =
                    availabilityAlarmCts;


                /*
                 * Desassocia imediatamente.
                 *
                 * Assim uma próxima disponibilidade pode
                 * iniciar uma nova sirene sem ficar presa
                 * à task anterior.
                 */
                availabilityAlarmCts =
                    null;
            }


            try
            {
                cts.Cancel();
            }
            catch
            {
            }


            /*
             * Interrompe imediatamente,
             * mesmo no meio dos 6 segundos.
             */
            StopAlertSound();


            Console.WriteLine();
            Console.WriteLine(
                "🔇 NORTE/SUL INDISPONÍVEIS — SIRENE DESLIGADA."
            );
        }


        // ================================================================
        // PARA O WAV
        // ================================================================

        private static void StopAlertSound()
        {
            try
            {
                if (
                    OperatingSystem.IsWindows()
                )
                {
                    PlaySound(
                        null,
                        IntPtr.Zero,
                        0
                    );
                }
            }
            catch
            {
            }
        }


        // ================================================================
        // CRIA URL /categoria/
        // ================================================================

        private static string BuildCategoryUrl(
            string matchUrl
        )
        {
            string result =
                matchUrl.Replace(
                    "/setores/",
                    "/categoria/",
                    StringComparison.OrdinalIgnoreCase
                );


            if (
                !result.Equals(
                    matchUrl,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return result;
            }


            string temp =
                matchUrl.TrimEnd('/');


            int lastSlash =
                temp.LastIndexOf('/');


            if (
                lastSlash > 0
            )
            {
                return
                    temp.Substring(
                        0,
                        lastSlash
                    )
                    +
                    "/categoria/";
            }


            return matchUrl;
        }


        // ================================================================
        // NAVEGA PARA CATEGORIA
        // ================================================================

        private static void NavigateToCategory(
            IWebDriver driver
        )
        {
            driver.Navigate()
                .GoToUrl(
                    CATEGORIA_URL
                );


            WaitForCategoryPage(
                driver,
                DOM_WAIT_MS
            );
        }


        // ================================================================
        // PROCURA COMPRAR
        // ================================================================

        private static IWebElement FindComprarButton(
            IWebDriver driver
        )
        {
            try
            {
                /*
                 * HTML REAL:
                 *
                 * <p class="btn btn-link">Comprar →</p>
                 */

                var comprarTexts =
                    driver.FindElements(
                        By.CssSelector(
                            "p.btn.btn-link"
                        )
                    );


                foreach (
                    IWebElement p
                    in comprarTexts
                )
                {
                    try
                    {
                        string text =
                            (
                                p.Text
                                ??
                                ""
                            )
                            .Trim();


                        if (
                            !text.StartsWith(
                                "Comprar",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            continue;
                        }


                        /*
                         * Sobe até o <a> clicável.
                         */
                        var links =
                            p.FindElements(
                                By.XPath(
                                    "./ancestor::a[1]"
                                )
                            );


                        if (
                            links.Count > 0
                        )
                        {
                            IWebElement link =
                                links[0];


                            if (
                                link.Displayed
                                &&
                                link.Enabled
                            )
                            {
                                return link;
                            }
                        }


                        /*
                         * Fallback.
                         */
                        if (
                            p.Displayed
                            &&
                            p.Enabled
                        )
                        {
                            return p;
                        }
                    }
                    catch (
                        StaleElementReferenceException
                    )
                    {
                    }
                    catch
                    {
                    }
                }


                return null;
            }
            catch
            {
                return null;
            }
        }


        // ================================================================
        // ESGOTADO
        // ================================================================

        private static bool IsCategorySoldOut(
            IWebDriver driver
        )
        {
            try
            {
                /*
                 * HTML REAL:
                 *
                 * <p class="text-white">Esgotado</p>
                 */

                var elements =
                    driver.FindElements(
                        By.CssSelector(
                            "p.text-white"
                        )
                    );


                foreach (
                    IWebElement element
                    in elements
                )
                {
                    try
                    {
                        string text =
                            (
                                element.Text
                                ??
                                ""
                            )
                            .Trim();


                        if (
                            text.Equals(
                                "Esgotado",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            return true;
                        }
                    }
                    catch (
                        StaleElementReferenceException
                    )
                    {
                    }
                }


                return false;
            }
            catch
            {
                return false;
            }
        }


        // ================================================================
        // CLICA COMPRAR
        // ================================================================

        private static bool ClickComprar(
            IWebDriver driver,
            IWebElement element
        )
        {
            try
            {
                element.Click();


                return true;
            }
            catch
            {
                try
                {
                    IJavaScriptExecutor js =
                        (IJavaScriptExecutor)driver;


                    js.ExecuteScript(
                        "arguments[0].click();",
                        element
                    );


                    return true;
                }
                catch (
                    Exception ex
                )
                {
                    Console.WriteLine(
                        "Erro ao clicar em COMPRAR: "
                        +
                        ex.Message
                    );


                    return false;
                }
            }
        }


        // ================================================================
        // ESPERA /setores/
        // ================================================================

        private static bool WaitUntilSectorsUnlocked(
            IWebDriver driver,
            int timeoutMs
        )
        {
            try
            {
                var wait =
                    new WebDriverWait(
                        driver,
                        TimeSpan.FromMilliseconds(
                            timeoutMs
                        )
                    );


                wait.PollingInterval =
                    TimeSpan.FromMilliseconds(
                        100
                    );


                wait.IgnoreExceptionTypes(
                    typeof(NoSuchElementException),
                    typeof(StaleElementReferenceException)
                );


                return wait.Until(
                    d =>
                    {
                        if (
                            IsAuthPage(
                                d
                            )
                        )
                        {
                            return false;
                        }


                        if (
                            IsSectorsPage(
                                d
                            )
                        )
                        {
                            return true;
                        }


                        bool norteExiste =
                            d.FindElements(
                                By.Id(
                                    "norte"
                                )
                            ).Count > 0;


                        bool sulExiste =
                            d.FindElements(
                                By.Id(
                                    "sul"
                                )
                            ).Count > 0;


                        return
                            norteExiste
                            ||
                            sulExiste;
                    }
                );
            }
            catch (
                WebDriverTimeoutException
            )
            {
                return false;
            }
            catch
            {
                return false;
            }
        }


        // ================================================================
        // ESPERA CATEGORIA
        // ================================================================

        private static void WaitForCategoryPage(
            IWebDriver driver,
            int timeoutMs
        )
        {
            try
            {
                var wait =
                    new WebDriverWait(
                        driver,
                        TimeSpan.FromMilliseconds(
                            timeoutMs
                        )
                    );


                wait.PollingInterval =
                    TimeSpan.FromMilliseconds(
                        100
                    );


                wait.IgnoreExceptionTypes(
                    typeof(NoSuchElementException),
                    typeof(StaleElementReferenceException)
                );


                wait.Until(
                    d =>
                    {
                        if (
                            IsAuthPage(
                                d
                            )
                        )
                        {
                            return true;
                        }


                        if (
                            !IsCategoryPage(
                                d
                            )
                        )
                        {
                            return true;
                        }


                        if (
                            FindComprarButton(
                                d
                            )
                            != null
                        )
                        {
                            return true;
                        }


                        if (
                            IsCategorySoldOut(
                                d
                            )
                        )
                        {
                            return true;
                        }


                        return false;
                    }
                );
            }
            catch (
                WebDriverTimeoutException
            )
            {
            }
        }


        // ================================================================
        // ESPERA NORTE/SUL
        // ================================================================

        private static void WaitForSectorsPage(
            IWebDriver driver,
            int timeoutMs
        )
        {
            try
            {
                var wait =
                    new WebDriverWait(
                        driver,
                        TimeSpan.FromMilliseconds(
                            timeoutMs
                        )
                    );


                wait.PollingInterval =
                    TimeSpan.FromMilliseconds(
                        100
                    );


                wait.IgnoreExceptionTypes(
                    typeof(NoSuchElementException),
                    typeof(StaleElementReferenceException)
                );


                wait.Until(
                    d =>
                    {
                        if (
                            IsAuthPage(
                                d
                            )
                        )
                        {
                            return true;
                        }


                        if (
                            !IsSectorsPage(
                                d
                            )
                        )
                        {
                            return true;
                        }


                        bool norteExiste =
                            d.FindElements(
                                By.Id(
                                    "norte"
                                )
                            ).Count > 0;


                        bool sulExiste =
                            d.FindElements(
                                By.Id(
                                    "sul"
                                )
                            ).Count > 0;


                        return
                            norteExiste
                            ||
                            sulExiste;
                    }
                );
            }
            catch (
                WebDriverTimeoutException
            )
            {
            }
        }


        // ================================================================
        // VERIFICA NORTE/SUL
        // ================================================================

        private static bool CheckSectorAvailability(
            IWebDriver driver,
            string elementId
        )
        {
            try
            {
                /*
                 * INDISPONÍVEL:
                 *
                 * <g
                 * class="svg tooltip sector norte disabled"
                 * id="norte">
                 *
                 *
                 * DISPONÍVEL:
                 *
                 * <a ...>
                 *   <g
                 *   class="svg tooltip sector norte"
                 *   id="norte">
                 * </a>
                 */

                var elements =
                    driver.FindElements(
                        By.Id(
                            elementId
                        )
                    );


                if (
                    elements.Count == 0
                )
                {
                    Console.WriteLine(
                        $"DEBUG: #{elementId} não encontrado."
                    );


                    return false;
                }


                IWebElement sector =
                    elements[0];


                string classAttribute =
                    sector.GetAttribute(
                        "class"
                    )
                    ??
                    "";


                /*
                 * disabled = indisponível.
                 */
                if (
                    classAttribute.Contains(
                        "disabled",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return false;
                }


                /*
                 * Segunda confirmação:
                 * disponível normalmente está dentro de <a>.
                 */
                try
                {
                    var parentLinks =
                        sector.FindElements(
                            By.XPath(
                                "./ancestor::*[local-name()='a'][1]"
                            )
                        );


                    if (
                        parentLinks.Count > 0
                    )
                    {
                        return true;
                    }
                }
                catch
                {
                }


                /*
                 * Ausência de disabled continua sendo
                 * o principal indicador.
                 */
                return true;
            }
            catch (
                StaleElementReferenceException
            )
            {
                return false;
            }
            catch (
                Exception ex
            )
            {
                Console.WriteLine(
                    $"Erro verificando #{elementId}: "
                    +
                    ex.Message
                );


                return false;
            }
        }


        // ================================================================
        // É /categoria/?
        // ================================================================

        private static bool IsCategoryPage(
            IWebDriver driver
        )
        {
            try
            {
                Uri uri =
                    new Uri(
                        driver.Url
                    );


                return
                    uri.AbsolutePath.Contains(
                        "/categoria",
                        StringComparison.OrdinalIgnoreCase
                    );
            }
            catch
            {
                return false;
            }
        }


        // ================================================================
        // É /setores/?
        // ================================================================

        private static bool IsSectorsPage(
            IWebDriver driver
        )
        {
            try
            {
                Uri uri =
                    new Uri(
                        driver.Url
                    );


                return
                    uri.AbsolutePath.Contains(
                        "/setores",
                        StringComparison.OrdinalIgnoreCase
                    );
            }
            catch
            {
                return false;
            }
        }


        // ================================================================
        // É LOGIN?
        // ================================================================

        private static bool IsAuthPage(
            IWebDriver driver
        )
        {
            try
            {
                string url =
                    driver.Url
                    ??
                    "";


                return
                    url.Contains(
                        "/auth/",
                        StringComparison.OrdinalIgnoreCase
                    )
                    ||
                    url.Contains(
                        "/login",
                        StringComparison.OrdinalIgnoreCase
                    );
            }
            catch
            {
                return false;
            }
        }


        // ================================================================
        // INTERVALO ENTRE REFRESHES
        // ================================================================

        private static void WaitRandomRefresh()
        {
            int wait =
                rnd.Next(
                    REFRESH_MIN_MS,
                    REFRESH_MAX_MS + 1
                );


            Console.WriteLine(
                $"Próximo refresh em " +
                $"{wait / 1000.0:F1}s..."
            );


            Thread.Sleep(
                wait
            );
        }


        // ================================================================
        // LOGIN / COOKIES
        // ================================================================

        private static bool LoginRoutine(
            IWebDriver driver
        )
        {
            if (
                File.Exists(
                    COOKIE_FILE
                )
            )
            {
                Console.WriteLine(
                    "Carregando sessão salva..."
                );


                try
                {
                    driver.Navigate()
                        .GoToUrl(
                            "https://www.fieltorcedor.com.br"
                        );


                    var cookies =
                        JsonConvert.DeserializeObject<
                            List<CookieData>
                        >(
                            File.ReadAllText(
                                COOKIE_FILE
                            )
                        );


                    if (
                        cookies != null
                    )
                    {
                        foreach (
                            CookieData cookieData
                            in cookies
                        )
                        {
                            if (
                                cookieData.Expiry.HasValue
                                &&
                                cookieData.Expiry.Value
                                <
                                DateTime.Now
                            )
                            {
                                continue;
                            }


                            try
                            {
                                driver.Manage()
                                    .Cookies
                                    .AddCookie(
                                        new Cookie(
                                            cookieData.Name,
                                            cookieData.Value,
                                            cookieData.Domain,
                                            cookieData.Path,
                                            cookieData.Expiry
                                        )
                                    );
                            }
                            catch
                            {
                            }
                        }
                    }


                    driver.Navigate()
                        .GoToUrl(
                            CATEGORIA_URL
                        );


                    WaitForCategoryPage(
                        driver,
                        2500
                    );


                    if (
                        !IsAuthPage(
                            driver
                        )
                    )
                    {
                        Console.WriteLine(
                            "Sessão restaurada com sucesso."
                        );


                        return true;
                    }


                    Console.WriteLine(
                        "Cookies expirados ou inválidos."
                    );
                }
                catch (
                    Exception ex
                )
                {
                    Console.WriteLine(
                        "Erro ao restaurar sessão: "
                        +
                        ex.Message
                    );
                }
            }


            // ============================================================
            // LOGIN MANUAL POR ENQUANTO
            // ============================================================

            Console.WriteLine();
            Console.WriteLine(
                "=================================================="
            );

            Console.WriteLine(
                "LOGIN NECESSÁRIO"
            );

            Console.WriteLine(
                "=================================================="
            );

            Console.WriteLine(
                "Faça o login no navegador."
            );

            Console.WriteLine(
                "Quando terminar, volte ao console e pressione ENTER."
            );


            driver.Navigate()
                .GoToUrl(
                    "https://www.fieltorcedor.com.br/auth/login"
                );


            Console.ReadLine();


            if (
                IsAuthPage(
                    driver
                )
            )
            {
                Console.WriteLine(
                    "Ainda estamos na página de login."
                );


                return false;
            }


            SaveCookies(
                driver
            );


            return true;
        }


        // ================================================================
        // SALVA COOKIES
        // ================================================================

        private static void SaveCookies(
            IWebDriver driver
        )
        {
            try
            {
                Console.WriteLine(
                    "Salvando nova sessão..."
                );


                var currentCookies =
                    driver.Manage()
                        .Cookies
                        .AllCookies;


                var cookieList =
                    new List<CookieData>();


                foreach (
                    Cookie c
                    in currentCookies
                )
                {
                    cookieList.Add(
                        new CookieData
                        {
                            Name =
                                c.Name,

                            Value =
                                c.Value,

                            Domain =
                                c.Domain,

                            Path =
                                c.Path,

                            Expiry =
                                c.Expiry,

                            Secure =
                                c.Secure
                        }
                    );
                }


                File.WriteAllText(
                    COOKIE_FILE,
                    JsonConvert.SerializeObject(
                        cookieList,
                        Formatting.Indented
                    )
                );


                Console.WriteLine(
                    "Sessão salva."
                );
            }
            catch (
                Exception ex
            )
            {
                Console.WriteLine(
                    "Erro ao salvar cookies: "
                    +
                    ex.Message
                );
            }
        }


        // ================================================================
        // COOKIE
        // ================================================================

        public class CookieData
        {
            public string Name
            {
                get;
                set;
            }


            public string Value
            {
                get;
                set;
            }


            public string Domain
            {
                get;
                set;
            }


            public string Path
            {
                get;
                set;
            }


            public DateTime? Expiry
            {
                get;
                set;
            }


            public bool Secure
            {
                get;
                set;
            }
        }
    }
}