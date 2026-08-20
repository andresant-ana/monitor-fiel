#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static string MATCH_URL;
        private static string CATEGORIA_URL;
        private static string TELEGRAM_BOT_TOKEN;
        private static string TELEGRAM_CHAT_ID;
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

        static async Task Main(string[] args)
        {
            Env.Load();

            MATCH_URL = Environment.GetEnvironmentVariable("MATCH_URL");
            TELEGRAM_BOT_TOKEN = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            TELEGRAM_CHAT_ID = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

            if (string.IsNullOrWhiteSpace(MATCH_URL) ||
                string.IsNullOrWhiteSpace(TELEGRAM_BOT_TOKEN) ||
                string.IsNullOrWhiteSpace(TELEGRAM_CHAT_ID))
            {
                Console.WriteLine("ERRO CRÍTICO: Variáveis de ambiente não encontradas. Verifique o arquivo .env.");
                return;
            }

            MATCH_URL = EnsureTrailingSlash(MATCH_URL);
            CATEGORIA_URL = BuildCategoryUrl(MATCH_URL);

            Console.WriteLine("Iniciando Monitor Fiel Torcedor (Versão V26)...");
            Console.WriteLine($"URL Setores  : {MATCH_URL}");
            Console.WriteLine($"URL Categoria: {CATEGORIA_URL}");
            Console.WriteLine($"Intervalo categoria: {CATEGORY_REFRESH_MIN_MS / 1000}-{CATEGORY_REFRESH_MAX_MS / 1000}s");
            Console.WriteLine($"Intervalo estádio  : {SECTOR_REFRESH_MIN_MS / 1000}-{SECTOR_REFRESH_MAX_MS / 1000}s");

            var options = new ChromeOptions();
            options.AddArgument("--start-maximized");

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

                while (true)
                {
                    try
                    {
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
                        }

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
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro no loop: {ex.Message}");
                        // Em erro inesperado, desacelera em vez de insistir rapidamente.
                        Thread.Sleep(15000);
                    }
                }
            }
            finally
            {
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao restaurar cookies: {ex.Message}");
                }
            }

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

        public class CookieData
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public DateTime? Expiry { get; set; }
            public bool Secure { get; set; }
        }
    }
}