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
using Telegram.Bot.Types;
using DotNetEnv;

namespace MonitorFiel
{
    class Program
    {
        private static string MATCH_URL;
        private static string TELEGRAM_BOT_TOKEN;
        private static string TELEGRAM_CHAT_ID;
        
        // Arquivo local pra salvar os cookies e não precisar logar toda vez
        private static string COOKIE_FILE = "session_cookies.json";

        static async Task Main(string[] args)
        {
            // Carrega as variáveis de ambiente do arquivo .env
            Env.Load();

            // Atribui os valores das variáveis de ambiente
            MATCH_URL = Environment.GetEnvironmentVariable("MATCH_URL");
            TELEGRAM_BOT_TOKEN = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            TELEGRAM_CHAT_ID = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

            // Validação simples pra garantir que o .env tá certo
            if (string.IsNullOrEmpty(MATCH_URL) || string.IsNullOrEmpty(TELEGRAM_BOT_TOKEN) || string.IsNullOrEmpty(TELEGRAM_CHAT_ID))
            {
                Console.WriteLine("ERRO CRÍTICO: Variáveis de ambiente não encontradas. Verifique o arquivo .env.");
                return;
            }

            Console.WriteLine("Iniciando Monitor Fiel Torcedor (Versão V22)...");

            var options = new ChromeOptions();
            options.AddArgument("--start-maximized");

            using (IWebDriver driver = new ChromeDriver(options))
            {
                // Tenta reaproveitar a sessão anterior
                bool loggedIn = LoginRoutine(driver);

                if (!loggedIn)
                {
                    Console.WriteLine("Falha crítica no login. Encerrando.");
                    return;
                }

                var botClient = new TelegramBotClient(TELEGRAM_BOT_TOKEN);

                Console.WriteLine($"Iniciando monitoramento para: {MATCH_URL}");
                Console.WriteLine("Pressione Ctrl+C para encerrar.");

                // Loop infinito de monitoramento
                while (true)
                {
                    try
                    {
                        driver.Navigate().GoToUrl(MATCH_URL);
                        
                        Thread.Sleep(5000);

                        // Se cair a sessão, refaço o login automático
                        if (driver.Url.Contains("login") || driver.Url.Contains("auth"))
                        {
                            Console.WriteLine("Sessão expirada durante o monitoramento. Refazendo login...");
                            LoginRoutine(driver);
                            continue;
                        }

                        bool norteDisponivel = CheckSectorAvailability(driver, "norte");
                        bool sulDisponivel = CheckSectorAvailability(driver, "sul");

                        if (norteDisponivel || sulDisponivel)
                        {
                            string msg = $"🚨 ALERTA FIEL! Ingressos Encontrados!\n";
                            if (norteDisponivel) msg += "✅ SETOR NORTE DISPONÍVEL\n";
                            if (sulDisponivel) msg += "✅ SETOR SUL DISPONÍVEL\n";
                            msg += $"\nCorra: {MATCH_URL}";
                            
                            Console.WriteLine("INGRESSO ENCONTRADO! Enviando Telegram...");
                            
                            await botClient.SendMessage(
                                chatId: TELEGRAM_CHAT_ID, 
                                text: msg
                            );
                            
                            Console.Beep(1000, 2000);
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Nada ainda. Norte: {(norteDisponivel ? "ON" : "OFF")} | Sul: {(sulDisponivel ? "ON" : "OFF")}");
                        }

                        // Delay aleatório pra evitar bloqueio do WAF
                        Random rnd = new Random();
                        int waitTime = rnd.Next(60000, 120001);
                        Console.WriteLine($"Aguardando {waitTime / 1000} segundos...");
                        Thread.Sleep(waitTime);

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro no loop: {ex.Message}");
                        Thread.Sleep(10000); 
                    }
                }
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
                    
                    driver.Navigate().GoToUrl(MATCH_URL);
                    Thread.Sleep(3000);

                    if (!driver.Url.Contains("login") && !driver.Url.Contains("auth"))
                    {
                        Console.WriteLine("Sessão restaurada com sucesso.");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao restaurar cookies: {ex.Message}");
                }
            }

            Console.WriteLine("--- ATENÇÃO NECESSÁRIA ---");
            Console.WriteLine("1. Faça o login manualmente no navegador que abriu.");
            Console.WriteLine("2. Resolva o Captcha.");
            Console.WriteLine("3. Navegue até a página inicial logada.");
            Console.WriteLine("4. VOLTE AQUI E APERTE [ENTER].");
            
            driver.Navigate().GoToUrl("https://www.fieltorcedor.com.br/auth/login");
            Console.ReadLine();

            Console.WriteLine("Salvando nova sessão...");
            var currentCookies = driver.Manage().Cookies.AllCookies;
            var cookieList = new List<CookieData>();
            foreach(var c in currentCookies)
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

        private static bool CheckSectorAvailability(IWebDriver driver, string elementId)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                var element = wait.Until(d => d.FindElement(By.Id(elementId)));
                string classAttribute = element.GetAttribute("class");
                
                // Se não tem a classe 'disabled', tá liberado
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