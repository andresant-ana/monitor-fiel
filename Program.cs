#nullable disable

using System;
using System.Runtime.InteropServices;
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
using Telegram.Bot.Types;
using DotNetEnv;

namespace MonitorFiel
{
    class Program
    {
        private static string MATCH_URL;
        private static string CATEGORIA_URL;
        private static string TELEGRAM_BOT_TOKEN;
        private static string TELEGRAM_CHAT_ID;
        private static string COOKIE_FILE = "session_cookies.json";

        static async Task Main(string[] args)
        {
            Env.Load();

            MATCH_URL = Environment.GetEnvironmentVariable("MATCH_URL");
            TELEGRAM_BOT_TOKEN = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            TELEGRAM_CHAT_ID = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

            if (string.IsNullOrEmpty(MATCH_URL) || string.IsNullOrEmpty(TELEGRAM_BOT_TOKEN) || string.IsNullOrEmpty(TELEGRAM_CHAT_ID))
            {
                Console.WriteLine("ERRO CRÍTICO: Variáveis de ambiente não encontradas. Verifique o arquivo .env.");
                return;
            }

            // Deriva a URL de categoria a partir da URL de setores
            // Ex: .../corinthians-x-palmeiras-br26/setores/ -> .../corinthians-x-palmeiras-br26/categoria/
            CATEGORIA_URL = MATCH_URL.Replace("/setores/", "/categoria/");
            if (CATEGORIA_URL == MATCH_URL)
            {
                // Fallback: se MATCH_URL já não tem /setores/, tenta construir
                CATEGORIA_URL = MATCH_URL.TrimEnd('/');
                int lastSlash = CATEGORIA_URL.LastIndexOf('/');
                if (lastSlash > 0)
                    CATEGORIA_URL = CATEGORIA_URL.Substring(0, lastSlash) + "/categoria/";
            }

            Console.WriteLine("Iniciando Monitor Fiel Torcedor (Versão V24)...");
            Console.WriteLine($"URL Setores  : {MATCH_URL}");
            Console.WriteLine($"URL Categoria: {CATEGORIA_URL}");

            var options = new ChromeOptions();
            options.AddArgument("--start-maximized");

            // Perfil persistente: salva login, cookies e senhas entre execuções
            string profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chrome-profile");
            Directory.CreateDirectory(profilePath);
            options.AddArgument($"--user-data-dir={profilePath}");

            IWebDriver driver = new ChromeDriver(options);

            // Garante que o navegador fecha ao encerrar com Ctrl+C
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nEncerrando... fechando navegador.");
                try { driver.Quit(); } catch { }
                Environment.Exit(0);
            };

            try
            {
                bool loggedIn = LoginRoutine(driver);

                if (!loggedIn)
                {
                    Console.WriteLine("Falha crítica no login. Encerrando.");
                    driver.Quit();
                    return;
                }

                Console.WriteLine("Navegando para a página de categoria do jogo...");
                driver.Navigate().GoToUrl(CATEGORIA_URL);
                Thread.Sleep(4000);
                Console.WriteLine($"Sessão ativa. URL atual: {driver.Url}");
                Console.WriteLine("Iniciando monitoramento. Pressione Ctrl+C para encerrar.");

                var botClient = new TelegramBotClient(TELEGRAM_BOT_TOKEN);
                bool monitoringSectors = false;

                while (true)
                {
                    try
                    {
                        if (IsAuthPage(driver.Url))
                        {
                            Console.WriteLine("Sessão expirada. Refazendo login...");

                            if (!LoginRoutine(driver))
                            {
                                Thread.Sleep(3000);
                                continue;
                            }

                            monitoringSectors = false;
                            driver.Navigate().GoToUrl(CATEGORIA_URL);
                            Thread.Sleep(3000);
                            continue;
                        }

                        if (!monitoringSectors)
                        {
                            // Durante a fase de categoria, o site pode redirecionar automaticamente
                            // para /jogos quando os ingressos estão esgotados. Mantemos o navegador
                            // preso à categoria correta e só abandonamos essa fase quando /setores/ abrir.
                            if (!EnsureCategoryPage(driver))
                            {
                                Thread.Sleep(1500);
                                continue;
                            }

                            bool temIngressoDisponivel = CheckIfAnyTicketAvailable(driver);

                            if (!temIngressoDisponivel)
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Esgotado. Permanecendo na página de categoria.");

                                int waitTime = Random.Shared.Next(12000, 18001);
                                Console.WriteLine($"Aguardando {waitTime / 1000}s antes de atualizar a categoria...");
                                WaitKeepingCategory(driver, waitTime);

                                if (IsAuthPage(driver.Url))
                                    continue;

                                if (IsSectorPage(driver.Url))
                                {
                                    monitoringSectors = true;
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Página de setores aberta. Entrando em modo de monitoramento dos setores.");
                                    continue;
                                }

                                if (IsCategoryPage(driver.Url))
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Atualizando página de categoria...");
                                    driver.Navigate().Refresh();
                                    Thread.Sleep(3000);
                                }

                                continue;
                            }

                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Disponibilidade detectada na categoria! Tentando acessar setores...");
                            driver.Navigate().GoToUrl(MATCH_URL);
                            Thread.Sleep(4000);

                            if (IsAuthPage(driver.Url))
                            {
                                Console.WriteLine("Redirecionado para login ao acessar setores. Refazendo login...");
                                continue;
                            }

                            if (IsSectorPage(driver.Url))
                            {
                                monitoringSectors = true;
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Acesso a /setores/ liberado. Permanecendo no mapa de setores.");
                                continue;
                            }

                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Setores ainda não acessíveis. URL atual: {driver.Url}");

                            if (!IsCategoryPage(driver.Url))
                            {
                                Console.WriteLine("Voltando para a página de categoria...");
                                driver.Navigate().GoToUrl(CATEGORIA_URL);
                                Thread.Sleep(2500);
                            }

                            continue;
                        }

                        // Uma vez que /setores/ abre, não voltamos deliberadamente para /categoria/.
                        // O monitor fica no mapa e atualiza a própria página até Norte ou Sul liberar.
                        if (!IsSectorPage(driver.Url))
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Saímos da página de setores ({driver.Url}). Voltando ao modo categoria.");
                            monitoringSectors = false;

                            if (!IsCategoryPage(driver.Url))
                            {
                                driver.Navigate().GoToUrl(CATEGORIA_URL);
                                Thread.Sleep(2500);
                            }

                            continue;
                        }

                        bool norteDisponivel = CheckSectorAvailability(driver, "norte");
                        bool sulDisponivel = CheckSectorAvailability(driver, "sul");

                        if (norteDisponivel || sulDisponivel)
                        {
                            string msg = "🚨 ALERTA FIEL! Ingressos Encontrados!\n";
                            if (norteDisponivel) msg += "✅ SETOR NORTE DISPONÍVEL\n";
                            if (sulDisponivel) msg += "✅ SETOR SUL DISPONÍVEL\n";
                            msg += $"\nCorra: {MATCH_URL}";

                            Console.WriteLine("🚨 INGRESSO ENCONTRADO! Enviando Telegram...");

                            await botClient.SendMessage(
                                chatId: TELEGRAM_CHAT_ID,
                                text: msg
                            );

                            Console.Beep(1000, 2000);
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Setores ainda indisponíveis. Norte={norteDisponivel} | Sul={sulDisponivel}");
                        }

                        int sectorWaitTime = Random.Shared.Next(12000, 18001);
                        Console.WriteLine($"Aguardando {sectorWaitTime / 1000}s antes de atualizar os setores...");
                        Thread.Sleep(sectorWaitTime);

                        if (IsSectorPage(driver.Url))
                        {
                            driver.Navigate().Refresh();
                            Thread.Sleep(3000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro no loop: {ex.Message}");
                        Thread.Sleep(10000);
                    }
                }
            }
            finally
            {
                try { driver.Quit(); } catch { }
            }
        }

        private static bool LoginRoutine(IWebDriver driver)
        {
            if (File.Exists(COOKIE_FILE))
            {
                Console.WriteLine("Carregando sessão salva...");
                try
                {
                    driver.Navigate().GoToUrl("https://www.fieltorcedor.com.br");
                    Thread.Sleep(2000);
                    var cookies = JsonConvert.DeserializeObject<List<CookieData>>(File.ReadAllText(COOKIE_FILE));
                    foreach (var cookieData in cookies)
                    {
                        if (cookieData.Expiry.HasValue && cookieData.Expiry < DateTime.Now) continue;

                        driver.Manage().Cookies.AddCookie(new Cookie(
                            cookieData.Name,
                            cookieData.Value,
                            cookieData.Domain,
                            cookieData.Path,
                            cookieData.Expiry));
                    }

                    driver.Navigate().GoToUrl(CATEGORIA_URL);
                    Thread.Sleep(4000);

                    if (!IsAuthPage(driver.Url))
                    {
                        Console.WriteLine("Sessão restaurada com sucesso.");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("Cookies expirados ou inválidos. Precisa fazer login manual.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao restaurar cookies: {ex.Message}");
                }
            }

            Console.WriteLine("--- ATENÇÃO NECESSÁRIA ---");
            Console.WriteLine("1. Faça o login manualmente no navegador que abriu.");
            Console.WriteLine("2. Resolva o Captcha se aparecer.");
            Console.WriteLine("3. Navegue até a página inicial logada (você verá seu nome no canto).");
            Console.WriteLine("4. VOLTE AQUI E APERTE [ENTER].");

            driver.Navigate().GoToUrl("https://www.fieltorcedor.com.br/auth/login");
            Console.ReadLine();

            Console.WriteLine("Salvando nova sessão...");
            var currentCookies = driver.Manage().Cookies.AllCookies;
            var cookieList = new List<CookieData>();
            foreach (var c in currentCookies)
            {
                cookieList.Add(new CookieData
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    Expiry = c.Expiry,
                    Secure = c.Secure
                });
            }

            File.WriteAllText(COOKIE_FILE, JsonConvert.SerializeObject(cookieList));
            Console.WriteLine("Sessão salva.");
            return true;
        }

        /// <summary>
        /// Garante que o navegador permaneça na categoria do jogo durante a fase
        /// de espera. Se o site expulsar o usuário para /jogos, volta imediatamente
        /// para a categoria configurada.
        /// </summary>
        private static bool EnsureCategoryPage(IWebDriver driver)
        {
            try
            {
                if (IsAuthPage(driver.Url))
                    return false;

                if (IsCategoryPage(driver.Url))
                    return true;

                if (IsSectorPage(driver.Url))
                    return false;

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Redirecionamento detectado para {driver.Url}");
                Console.WriteLine("Voltando imediatamente para a página de categoria...");

                driver.Navigate().GoToUrl(CATEGORIA_URL);
                Thread.Sleep(2500);

                return IsCategoryPage(driver.Url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao garantir página de categoria: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Aguarda o próximo refresh observando apenas a URL local do navegador.
        /// Se o site redirecionar a aba para /jogos durante a espera, retorna para
        /// a categoria imediatamente sem aguardar o restante do intervalo.
        /// </summary>
        private static void WaitKeepingCategory(IWebDriver driver, int milliseconds)
        {
            int elapsed = 0;
            const int interval = 500;

            while (elapsed < milliseconds)
            {
                Thread.Sleep(interval);
                elapsed += interval;

                string currentUrl = driver.Url;

                if (IsAuthPage(currentUrl) || IsSectorPage(currentUrl))
                    return;

                if (!IsCategoryPage(currentUrl))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Site redirecionou para {currentUrl}");
                    Console.WriteLine("Reabrindo a categoria imediatamente...");

                    driver.Navigate().GoToUrl(CATEGORIA_URL);
                    Thread.Sleep(2000);
                    return;
                }
            }
        }

        private static bool IsAuthPage(string url)
        {
            return url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("/auth", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCategoryPage(string url)
        {
            return url.Contains("/categoria/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSectorPage(string url)
        {
            return url.Contains("/setores/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifica na página de categoria se existe acesso real aos setores.
        /// Primeiro procura um link para /setores/ e usa o card enabled como fallback.
        /// </summary>
        private static bool CheckIfAnyTicketAvailable(IWebDriver driver)
        {
            try
            {
                var sectorLinks = driver.FindElements(
                    By.CssSelector("#main-content a[href*='/setores/']")
                );

                if (sectorLinks.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Link para /setores/ encontrado na página de categoria.");
                    return true;
                }

                var enabledCards = driver.FindElements(
                    By.CssSelector("#main-content .meuplano-card.enabled")
                );

                if (enabledCards.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {enabledCards.Count} card(s) habilitado(s) encontrado(s) na página de categoria.");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro em CheckIfAnyTicketAvailable: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Na tela de setores, verifica se o setor (por id) NÃO tem a classe "disabled".
        /// </summary>
        private static bool CheckSectorAvailability(IWebDriver driver, string elementId)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                var element = wait.Until(d => d.FindElement(By.Id(elementId)));
                string classAttribute = element.GetAttribute("class");
                return !classAttribute.Contains("disabled");
            }
            catch
            {
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