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
                // Remove último segmento e substitui por /categoria/
                int lastSlash = CATEGORIA_URL.LastIndexOf('/');
                if (lastSlash > 0)
                    CATEGORIA_URL = CATEGORIA_URL.Substring(0, lastSlash) + "/categoria/";
            }

            Console.WriteLine($"Iniciando Monitor Fiel Torcedor (Versão V23)...");
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

                while (true)
                {
                    try
                    {
                        driver.Navigate().GoToUrl(CATEGORIA_URL);
                        Thread.Sleep(5000);

                        if (driver.Url.Contains("login") || driver.Url.Contains("auth"))
                        {
                            Console.WriteLine("Sessão expirada. Refazendo login...");
                            LoginRoutine(driver);
                            continue;
                        }

                        bool temIngressoDisponivel = CheckIfAnyTicketAvailable(driver);

                        if (!temIngressoDisponivel)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Esgotado na página de categoria. Aguardando...");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Algum ingresso disponível na categoria! Verificando setor Norte...");

                            driver.Navigate().GoToUrl(MATCH_URL);
                            Thread.Sleep(5000);

                            if (driver.Url.Contains("login") || driver.Url.Contains("auth"))
                            {
                                Console.WriteLine("Redirecionado para login ao acessar setores. Refazendo login...");
                                LoginRoutine(driver);
                                continue;
                            }

                            if (driver.Url.Contains("/categoria/"))
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Redirecionado para categoria. Setores ainda não disponíveis para este plano.");
                            }
                            else
                            {
                                bool norteDisponivel = CheckSectorAvailability(driver, "norte");
                                bool sulDisponivel = CheckSectorAvailability(driver, "sul");

                                if (norteDisponivel || sulDisponivel)
                                {
                                    string msg = $"🚨 ALERTA FIEL! Ingressos Encontrados!\n";
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
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Na tela de setores: Norte={norteDisponivel} | Sul={sulDisponivel}");
                                }
                            }
                        }

                        Random rnd = new Random();
                        int waitTime = rnd.Next(12000, 18001);
                        Console.WriteLine($"Aguardando {waitTime / 1000}s até próxima verificação...");
                        Thread.Sleep(waitTime);
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

                    if (!driver.Url.Contains("login") && !driver.Url.Contains("auth"))
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
        /// Verifica na página de categoria se algum card tem o botão "COMPRAR"
        /// (ou seja, não está no estado "Esgotado").
        /// Estratégia: procura por um link que aponte para /setores/ — isso só existe quando tickets disponíveis.
        /// Fallback: procura por texto "COMPRAR" que não seja "COMPRE AGORA" do menu.
        /// </summary>
        private static bool CheckIfAnyTicketAvailable(IWebDriver driver)
        {
            try
            {
                // O card disponível tem a classe "enabled". O esgotado tem "disabled".
                // Procura dentro do #main-content para não pegar falsos positivos de outras partes da página.
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