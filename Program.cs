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
<<<<<<< HEAD

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
=======
        private static readonly string COOKIE_FILE = "session_cookies.json";

        // Intervalos normais de monitoramento. Mantemos jitter para não gerar um padrão fixo.
        // Não existe intervalo que garanta ausência de bloqueio; por isso há backoff automático
        // quando a página apresenta sinais típicos de rate limit/bloqueio.
        private const int CATEGORY_REFRESH_MIN_MS = 8000;
        private const int CATEGORY_REFRESH_MAX_MS = 12000;
        private const int SECTOR_REFRESH_MIN_MS = 6000;
        private const int SECTOR_REFRESH_MAX_MS = 9000;
        private const int RATE_LIMIT_BACKOFF_MIN_MS = 60000;
        private const int RATE_LIMIT_BACKOFF_MAX_MS = 120000;

        private const int CATEGORY_CARD_WAIT_MS = 1800;
        private const int CATEGORY_FALLBACK_REFRESH_MIN_MS = 3000;
        private const int CATEGORY_FALLBACK_REFRESH_MAX_MS = 4500;
        private const int PAGE_SETTLE_MS = 1800;
        private const int RECOVERY_WAIT_MS = 1200;
        private const int LOCAL_ALARM_SECONDS = 30;

        private static bool scriptFreezeSupported = true;
        private static int localAlarmRunning = 0;

        private enum MonitorMode
        {
            Category,
            Sectors
        }

        private enum CategoryState
        {
            Unknown,
            SoldOut,
            BuyAvailable
        }
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a

        static async Task Main(string[] args)
        {
            Env.Load();


<<<<<<< HEAD
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
=======
            if (string.IsNullOrWhiteSpace(MATCH_URL) ||
                string.IsNullOrWhiteSpace(TELEGRAM_BOT_TOKEN) ||
                string.IsNullOrWhiteSpace(TELEGRAM_CHAT_ID))
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a
            {
                Console.WriteLine(
                    "ERRO CRÍTICO: verifique MATCH_URL, " +
                    "TELEGRAM_BOT_TOKEN e TELEGRAM_CHAT_ID " +
                    "no arquivo .env."
                );

                return;
            }

<<<<<<< HEAD

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

=======
            MATCH_URL = EnsureTrailingSlash(MATCH_URL);
            CATEGORIA_URL = BuildCategoryUrl(MATCH_URL);

            Console.WriteLine("Iniciando Monitor Fiel Torcedor (Versão V26)...");
            Console.WriteLine($"URL Setores  : {MATCH_URL}");
            Console.WriteLine($"URL Categoria: {CATEGORIA_URL}");
            Console.WriteLine($"Intervalo categoria: {CATEGORY_REFRESH_MIN_MS / 1000}-{CATEGORY_REFRESH_MAX_MS / 1000}s");
            Console.WriteLine($"Intervalo estádio  : {SECTOR_REFRESH_MIN_MS / 1000}-{SECTOR_REFRESH_MAX_MS / 1000}s");
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a

            // ============================================================
            // TESTE OPCIONAL DO SOM
            // ============================================================

<<<<<<< HEAD
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
=======
            string profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chrome-profile");
            Directory.CreateDirectory(profilePath);
            options.AddArgument($"--user-data-dir={profilePath}");

            var driver = new ChromeDriver(options);

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nEncerrando... fechando navegador.");
                try { EnablePageScripts(driver); } catch { }
                try { driver.Quit(); } catch { }
                Environment.Exit(0);
            };

            try
            {
                if (!LoginRoutine(driver))
                {
                    Console.WriteLine("Falha crítica no login. Encerrando.");
                    return;
                }

                var botClient = new TelegramBotClient(TELEGRAM_BOT_TOKEN);
                MonitorMode mode = MonitorMode.Category;
                bool previousNorthAvailable = false;
                bool previousSouthAvailable = false;

                Console.WriteLine("Sessão ativa. Iniciando monitoramento. Pressione Ctrl+C para encerrar.");

                // Se esta mesma sessão já conquistou acesso a /setores/ anteriormente,
                // o Fiel costuma manter esse acesso mesmo que o card volte a "Esgotado".
                // Testamos isso uma vez antes de entrar no modo de categoria.
                if (TryOpenSectors(driver))
                {
                    mode = MonitorMode.Sectors;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ A sessão já possui acesso a /setores/. Entrando direto no estádio.");
                }
                else
                {
                    EnterPinnedCategory(driver);
                }
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a

                while (true)
                {
                    try
                    {
<<<<<<< HEAD
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
=======
                        if (IsAuthPage(driver.Url))
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sessão expirada. Refazendo login...");
                            EnablePageScripts(driver);

                            if (!LoginRoutine(driver))
                            {
                                Thread.Sleep(3000);
                                continue;
                            }

                            // Uma sessão nova perde a autorização temporária de /setores/.
                            mode = MonitorMode.Category;
                            previousNorthAvailable = false;
                            previousSouthAvailable = false;

                            if (TryOpenSectors(driver))
                            {
                                mode = MonitorMode.Sectors;
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ A nova sessão já conseguiu reabrir /setores/.");
                            }
                            else
                            {
                                EnterPinnedCategory(driver);
                            }

                            continue;
                        }

                        // Se a aplicação enxergar uma página típica de bloqueio/rate limit, desacelera
                        // bastante antes de tentar novamente. Isso não tenta contornar o bloqueio;
                        // apenas reduz a pressão sobre o site.
                        if (LooksRateLimitedOrBlocked(driver))
                        {
                            int cooldown = Random.Shared.Next(RATE_LIMIT_BACKOFF_MIN_MS, RATE_LIMIT_BACKOFF_MAX_MS + 1);
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Possível rate limit/bloqueio detectado. Pausando {cooldown / 1000}s antes de continuar.");
                            EnablePageScripts(driver);
                            Thread.Sleep(cooldown);

                            if (mode == MonitorMode.Category && !IsAuthPage(driver.Url))
                                EnterPinnedCategory(driver);

                            continue;
                        }

                        if (mode == MonitorMode.Category)
                        {
                            if (!EnsurePinnedCategory(driver))
                            {
                                Thread.Sleep(RECOVERY_WAIT_MS);
                                continue;
                            }

                            CategoryState categoryState = DetectCategoryState(driver);

                            if (categoryState == CategoryState.BuyAvailable)
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🚨 Botão COMPRAR encontrado. Tentando abrir /setores/ imediatamente...");
                                EnablePageScripts(driver);

                                if (TryOpenSectors(driver))
                                {
                                    mode = MonitorMode.Sectors;
                                    previousNorthAvailable = false;
                                    previousSouthAvailable = false;
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ /setores/ liberado. A partir de agora o monitor fica no estádio até perder a sessão/acesso.");
                                    continue;
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a
                                }

                                if (IsAuthPage(driver.Url))
                                    continue;

                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] COMPRAR apareceu, mas /setores/ ainda não abriu. Voltando ao modo categoria.");
                                EnterPinnedCategory(driver);
                                continue;
                            }

                            if (categoryState == CategoryState.SoldOut)
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Esgotado. Categoria travada em {CATEGORIA_URL}");
                            }
                            else
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Estado da categoria ainda não identificado. Mantendo a rota e tentando novamente.");
                            }

                            int categoryWaitTime = scriptFreezeSupported
                                ? Random.Shared.Next(CATEGORY_REFRESH_MIN_MS, CATEGORY_REFRESH_MAX_MS + 1)
                                : Random.Shared.Next(CATEGORY_FALLBACK_REFRESH_MIN_MS, CATEGORY_FALLBACK_REFRESH_MAX_MS + 1);

                            if (scriptFreezeSupported)
                            {
                                Console.WriteLine($"Aguardando {categoryWaitTime / 1000.0:F1}s antes de atualizar a categoria...");
                            }
                            else
                            {
                                Console.WriteLine($"Proteção por CDP indisponível; atualizando em {categoryWaitTime / 1000.0:F1}s para manter a categoria ativa...");
                            }

                            WaitWhilePinnedToCategory(driver, categoryWaitTime);

                            if (IsAuthPage(driver.Url))
                                continue;

                            if (!IsCategoryPage(driver.Url))
                            {
                                // Fallback caso o site tenha conseguido sair da categoria.
                                EnterPinnedCategory(driver);
                                continue;
                            }

                            RefreshPinnedCategory(driver);
                            continue;
                        }

                        // MODO SETORES:
                        // depois que a sessão obteve /setores/, não consultamos mais o card COMPRAR.
                        // Mesmo se ele voltar a "Esgotado", continuamos atualizando o estádio.
                        if (!IsSectorPage(driver.Url))
                        {
                            if (IsAuthPage(driver.Url))
                                continue;

                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Saímos de /setores/ ({driver.Url}). Tentando reabrir com a mesma sessão...");
                            EnablePageScripts(driver);

                            if (TryOpenSectors(driver))
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] /setores/ reaberto com sucesso.");
                            }
                            else
                            {
                                if (IsAuthPage(driver.Url))
                                    continue;

                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] A sessão perdeu o acesso a /setores/. Voltando a esperar COMPRAR na categoria.");
                                mode = MonitorMode.Category;
                                previousNorthAvailable = false;
                                previousSouthAvailable = false;
                                EnterPinnedCategory(driver);
                                continue;
                            }

<<<<<<< HEAD

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
=======
                        bool northAvailable = CheckSectorAvailability(driver, "norte");
                        bool southAvailable = CheckSectorAvailability(driver, "sul");

                        bool northJustOpened = northAvailable && !previousNorthAvailable;
                        bool southJustOpened = southAvailable && !previousSouthAvailable;

                        if (northJustOpened || southJustOpened)
                        {
                            string message = "🚨 ALERTA FIEL! Ingressos Encontrados!\n";
                            if (northAvailable) message += "✅ SETOR NORTE DISPONÍVEL\n";
                            if (southAvailable) message += "✅ SETOR SUL DISPONÍVEL\n";
                            message += $"\nCorra: {MATCH_URL}";

                            Console.WriteLine("🚨 INGRESSO ENCONTRADO! Disparando alarme local e Telegram...");

                            // O alarme roda em background para não interromper o monitoramento.
                            StartLocalAlarm();

                            try
                            {
                                await botClient.SendMessage(
                                    chatId: TELEGRAM_CHAT_ID,
                                    text: message
                                );
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Falha ao enviar alerta no Telegram: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Estádio ativo. Norte={northAvailable} | Sul={southAvailable}");
                        }

                        previousNorthAvailable = northAvailable;
                        previousSouthAvailable = southAvailable;

                        int sectorWaitTime = Random.Shared.Next(SECTOR_REFRESH_MIN_MS, SECTOR_REFRESH_MAX_MS + 1);
                        Console.WriteLine($"Aguardando {sectorWaitTime / 1000.0:F1}s antes de atualizar o estádio...");
                        Thread.Sleep(sectorWaitTime);

                        if (IsAuthPage(driver.Url))
                            continue;

                        if (IsSectorPage(driver.Url))
                        {
                            driver.Navigate().Refresh();
                            Thread.Sleep(PAGE_SETTLE_MS);
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a
                        }
                    }
                    catch (
                        WebDriverException ex
                    )
                    {
<<<<<<< HEAD
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
=======
                        Console.WriteLine($"Erro no loop: {ex.Message}");
                        // Em erro inesperado, desacelera em vez de insistir rapidamente.
                        Thread.Sleep(15000);
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a
                    }
                }
            }
            finally
            {
<<<<<<< HEAD
                StopAvailabilityAlarm();

                StopAlertSound();


                try
                {
                    driver.Quit();
=======
                try { EnablePageScripts(driver); } catch { }
                try { driver.Quit(); } catch { }
            }
        }

        private static string EnsureTrailingSlash(string url)
        {
            return url.EndsWith("/") ? url : url + "/";
        }

        private static string BuildCategoryUrl(string matchUrl)
        {
            if (matchUrl.Contains("/setores/", StringComparison.OrdinalIgnoreCase))
            {
                int index = matchUrl.IndexOf("/setores/", StringComparison.OrdinalIgnoreCase);
                return matchUrl.Substring(0, index) + "/categoria/";
            }

            string trimmed = matchUrl.TrimEnd('/');
            int lastSlash = trimmed.LastIndexOf('/');

            if (lastSlash <= 0)
                throw new InvalidOperationException("MATCH_URL inválida. Informe a URL /setores/ do jogo.");

            return trimmed.Substring(0, lastSlash) + "/categoria/";
        }

        private static bool LoginRoutine(ChromeDriver driver)
        {
            EnablePageScripts(driver);

            if (File.Exists(COOKIE_FILE))
            {
                Console.WriteLine("Carregando sessão salva...");
                try
                {
                    driver.Navigate().GoToUrl("https://www.fieltorcedor.com.br");
                    Thread.Sleep(1200);

                    var cookies = JsonConvert.DeserializeObject<List<CookieData>>(File.ReadAllText(COOKIE_FILE))
                                  ?? new List<CookieData>();

                    foreach (var cookieData in cookies)
                    {
                        if (cookieData.Expiry.HasValue && cookieData.Expiry < DateTime.Now)
                            continue;

                        try
                        {
                            driver.Manage().Cookies.AddCookie(new Cookie(
                                cookieData.Name,
                                cookieData.Value,
                                cookieData.Domain,
                                cookieData.Path,
                                cookieData.Expiry));
                        }
                        catch
                        {
                            // Um cookie isolado inválido não deve impedir a restauração dos demais.
                        }
                    }

                    driver.Navigate().GoToUrl(CATEGORIA_URL);
                    Thread.Sleep(1200);

                    if (!IsAuthPage(driver.Url))
                    {
                        Console.WriteLine("Sessão restaurada com sucesso.");
                        return true;
                    }

                    Console.WriteLine("Cookies expirados ou inválidos. Precisa fazer login manual.");
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a
                }
                catch
                {
                }
            }
<<<<<<< HEAD
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
=======

            Console.WriteLine("--- ATENÇÃO NECESSÁRIA ---");
            Console.WriteLine("1. Faça o login manualmente no navegador que abriu.");
            Console.WriteLine("2. Resolva o Captcha se aparecer.");
            Console.WriteLine("3. Aguarde estar realmente logado.");
            Console.WriteLine("4. VOLTE AQUI E APERTE [ENTER].");

            driver.Navigate().GoToUrl("https://www.fieltorcedor.com.br/auth/login");
            Console.ReadLine();

            // Valida o login antes de aceitar a sessão.
            driver.Navigate().GoToUrl(CATEGORIA_URL);
            Thread.Sleep(1200);

            if (IsAuthPage(driver.Url))
            {
                Console.WriteLine("O login ainda não foi concluído. Tente novamente.");
                return false;
            }

            SaveSessionCookies(driver);
            Console.WriteLine("Sessão salva.");
            return true;
        }

        private static void SaveSessionCookies(IWebDriver driver)
        {
            var cookieList = driver.Manage().Cookies.AllCookies
                .Select(c => new CookieData
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    Expiry = c.Expiry,
                    Secure = c.Secure
                })
                .ToList();

            File.WriteAllText(COOKIE_FILE, JsonConvert.SerializeObject(cookieList));
        }

        /// <summary>
        /// Tenta acessar /setores/ com a sessão atual. Se a sessão já tiver conquistado
        /// esse acesso anteriormente, a página abre mesmo que o card tenha voltado a Esgotado.
        /// </summary>
        private static bool TryOpenSectors(ChromeDriver driver)
        {
            try
            {
                EnablePageScripts(driver);
                driver.Navigate().GoToUrl(MATCH_URL);
                Thread.Sleep(PAGE_SETTLE_MS);
                return IsSectorPage(driver.Url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao tentar abrir /setores/: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Entra na categoria e desativa a execução de JavaScript da página via CDP.
        /// O redirecionamento automático para /jogos é client-side; com os scripts
        /// congelados, a aba permanece na categoria sem precisar fazer refresh agressivo.
        /// </summary>
        private static bool EnterPinnedCategory(ChromeDriver driver)
        {
            try
            {
                if (!IsCategoryPage(driver.Url))
                {
                    EnablePageScripts(driver);
                    driver.Navigate().GoToUrl(CATEGORIA_URL);
                }

                if (IsAuthPage(driver.Url))
                    return false;

                // GoToUrl/redirect já terminou de montar a página. Congelamos os scripts
                // imediatamente, antes do timer client-side conseguir mandar a aba para /jogos.
                FreezePageScripts(driver);
                WaitForCategoryCard(driver, CATEGORY_CARD_WAIT_MS);

                // Se o redirecionamento venceu a corrida, voltamos com os scripts já congelados.
                if (!IsCategoryPage(driver.Url) && !IsAuthPage(driver.Url))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Redirecionamento interceptado ({driver.Url}). Reabrindo categoria com scripts congelados...");
                    driver.Navigate().GoToUrl(CATEGORIA_URL);
                    FreezePageScripts(driver);
                    WaitForCategoryCard(driver, CATEGORY_CARD_WAIT_MS);
                }

                return IsCategoryPage(driver.Url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao fixar a página de categoria: {ex.Message}");
                return false;
            }
        }

        private static bool EnsurePinnedCategory(ChromeDriver driver)
        {
            if (IsAuthPage(driver.Url))
                return false;

            if (IsCategoryPage(driver.Url))
            {
                FreezePageScripts(driver);
                return true;
            }

            if (IsSectorPage(driver.Url))
                return false;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Site saiu da categoria para {driver.Url}. Forçando retorno...");

            // Se o CDP estiver disponível, navegamos de volta com JavaScript congelado.
            FreezePageScripts(driver);
            driver.Navigate().GoToUrl(CATEGORIA_URL);
            WaitForCategoryCard(driver, CATEGORY_CARD_WAIT_MS);

            return IsCategoryPage(driver.Url);
        }

        private static void RefreshPinnedCategory(ChromeDriver driver)
        {
            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Atualizando {CATEGORIA_URL}");

                // Permitimos que o carregamento normal da página execute seus scripts e, assim que
                // o refresh termina, congelamos novamente antes do redirecionamento atrasado para /jogos.
                EnablePageScripts(driver);
                driver.Navigate().Refresh();
                FreezePageScripts(driver);
                WaitForCategoryCard(driver, CATEGORY_CARD_WAIT_MS);

                if (!IsCategoryPage(driver.Url) && !IsAuthPage(driver.Url))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] O site tentou sair da categoria durante o refresh. Forçando retorno...");
                    driver.Navigate().GoToUrl(CATEGORIA_URL);
                    FreezePageScripts(driver);
                    WaitForCategoryCard(driver, CATEGORY_CARD_WAIT_MS);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar categoria: {ex.Message}");
            }
        }

        private static void WaitWhilePinnedToCategory(ChromeDriver driver, int milliseconds)
        {
            int elapsed = 0;
            const int pollMs = 150;

            while (elapsed < milliseconds)
            {
                Thread.Sleep(pollMs);
                elapsed += pollMs;

                string currentUrl = driver.Url;

                if (IsAuthPage(currentUrl) || IsSectorPage(currentUrl))
                    return;

                if (!IsCategoryPage(currentUrl))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Redirecionamento para {currentUrl} detectado. Voltando à categoria agora...");
                    FreezePageScripts(driver);
                    driver.Navigate().GoToUrl(CATEGORIA_URL);
                    WaitForCategoryCard(driver, CATEGORY_CARD_WAIT_MS);
                    FreezePageScripts(driver);
                    return;
                }
            }
        }

        private static void WaitForCategoryCard(IWebDriver driver, int timeoutMs)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                wait.Until(d =>
                {
                    if (!IsCategoryPage(d.Url))
                        return true;

                    var cards = d.FindElements(By.CssSelector("#main-content .meuplano-card"));
                    if (cards.Count > 0)
                        return true;

                    var mainContents = d.FindElements(By.Id("main-content"));
                    if (mainContents.Count == 0)
                        return false;

                    string text = mainContents[0].Text ?? string.Empty;
                    return text.Contains("Esgotado", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("COMPRAR", StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
                // A detecção de estado possui fallback por texto/links; timeout aqui não encerra o loop.
            }
        }

        private static CategoryState DetectCategoryState(IWebDriver driver)
        {
            try
            {
                if (!IsCategoryPage(driver.Url))
                    return CategoryState.Unknown;

                var mainContents = driver.FindElements(By.Id("main-content"));
                if (mainContents.Count == 0)
                    return CategoryState.Unknown;

                IWebElement main = mainContents[0];
                string mainText = main.Text ?? string.Empty;

                // Condição mais forte: existe link visível que leva para /setores/.
                var sectorLinks = main.FindElements(By.CssSelector("a[href*='/setores/']"));
                if (sectorLinks.Any(link => IsElementVisible(link)))
                    return CategoryState.BuyAvailable;

                // O card mostrado pelo site exibe literalmente COMPRAR quando liberado.
                // A busca fica restrita ao main-content para não confundir com o menu "Comprar ingressos".
                bool hasBuyText = mainText
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Any(line => line.Equals("COMPRAR", StringComparison.OrdinalIgnoreCase) ||
                                 line.StartsWith("COMPRAR ", StringComparison.OrdinalIgnoreCase));

                if (hasBuyText)
                    return CategoryState.BuyAvailable;

                if (mainText.Contains("Esgotado", StringComparison.OrdinalIgnoreCase))
                    return CategoryState.SoldOut;

                // Fallback para a classe já conhecida no HTML do Fiel.
                var enabledCards = main.FindElements(By.CssSelector(".meuplano-card.enabled"));
                if (enabledCards.Count > 0)
                    return CategoryState.BuyAvailable;

                var disabledCards = main.FindElements(By.CssSelector(".meuplano-card.disabled"));
                if (disabledCards.Count > 0)
                    return CategoryState.SoldOut;

                return CategoryState.Unknown;
            }
            catch (StaleElementReferenceException)
            {
                return CategoryState.Unknown;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao detectar estado da categoria: {ex.Message}");
                return CategoryState.Unknown;
            }
        }

        private static bool IsElementVisible(IWebElement element)
        {
            try
            {
                return element.Displayed;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Congela a execução de JavaScript no documento atual. Isso impede o timer
        /// client-side responsável por mandar a categoria esgotada de volta para /jogos.
        /// Se o Chrome/CDP não aceitar o comando, o monitor usa refresh rápido como fallback.
        /// </summary>
        private static void FreezePageScripts(ChromeDriver driver)
        {
            if (!scriptFreezeSupported)
                return;

            try
            {
                driver.ExecuteCdpCommand(
                    "Emulation.setScriptExecutionDisabled",
                    new Dictionary<string, object> { ["value"] = true }
                );
            }
            catch (Exception ex)
            {
                scriptFreezeSupported = false;
                Console.WriteLine($"Aviso: não foi possível congelar JavaScript via CDP ({ex.Message}). Usando modo de refresh rápido.");
            }
        }

        private static void EnablePageScripts(ChromeDriver driver)
        {
            if (!scriptFreezeSupported)
                return;

            try
            {
                driver.ExecuteCdpCommand(
                    "Emulation.setScriptExecutionDisabled",
                    new Dictionary<string, object> { ["value"] = false }
                );
            }
            catch (Exception ex)
            {
                scriptFreezeSupported = false;
                Console.WriteLine($"Aviso: não foi possível reativar JavaScript via CDP ({ex.Message}).");
            }
        }

        /// <summary>
        /// Detecta algumas respostas comuns de proteção/rate limit. O objetivo é somente
        /// desacelerar automaticamente quando o site demonstra que não quer mais tráfego.
        /// </summary>
        private static bool LooksRateLimitedOrBlocked(IWebDriver driver)
        {
            try
            {
                string title = driver.Title ?? string.Empty;
                string source = driver.PageSource ?? string.Empty;
                string sample = (title + "\n" + source).ToLowerInvariant();

                string[] markers =
                {
                    "too many requests",
                    "rate limit",
                    "access denied",
                    "temporarily blocked",
                    "request blocked",
                    "error 429",
                    "http 429"
                };

                return markers.Any(sample.Contains);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Dispara um alarme insistente no computador sem bloquear o loop principal.
        /// O volume final depende do volume do sistema operacional/dispositivo de áudio.
        /// </summary>
        private static void StartLocalAlarm()
        {
            if (Interlocked.Exchange(ref localAlarmRunning, 1) == 1)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    Console.WriteLine($"🔊 ALARME LOCAL ATIVO por aproximadamente {LOCAL_ALARM_SECONDS}s!");
                    DateTime until = DateTime.UtcNow.AddSeconds(LOCAL_ALARM_SECONDS);

                    while (DateTime.UtcNow < until)
                    {
                        try
                        {
                            Console.Beep(2000, 450);
                            Console.Beep(1200, 450);
                            Console.Beep(2200, 450);
                            Console.Beep(1000, 450);
                        }
                        catch
                        {
                            // Fallback para terminais/sistemas onde Console.Beep não é suportado.
                            Console.Write('\a');
                            Thread.Sleep(900);
                        }

                        Thread.Sleep(120);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref localAlarmRunning, 0);
                }
            });
        }

        private static bool IsAuthPage(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   (url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("/auth", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCategoryPage(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   url.StartsWith(CATEGORIA_URL.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSectorPage(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   url.StartsWith(MATCH_URL.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool CheckSectorAvailability(IWebDriver driver, string elementId)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(4));
                var element = wait.Until(d => d.FindElement(By.Id(elementId)));
                string classAttribute = element.GetAttribute("class") ?? string.Empty;
                return !classAttribute.Contains("disabled", StringComparison.OrdinalIgnoreCase);
>>>>>>> 121f284bb0a67822e1a70f5504a29a6127c4511a
            }
            catch (WebDriverTimeoutException)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Setor '{elementId}' não encontrado a tempo.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro verificando setor '{elementId}': {ex.Message}");
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