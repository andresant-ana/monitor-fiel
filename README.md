# MonitorFiel

O **MonitorFiel** é uma solução de automação desenvolvida em .NET (C#) para monitorar a disponibilidade de ingressos no programa Fiel Torcedor do Sport Club Corinthians Paulista.

O sistema utiliza **Selenium WebDriver** para navegação e scraping, contornando a necessidade de verificação constante manual. O foco principal é identificar a liberação de ingressos nos setores **Norte** e **Sul** da Neo Química Arena e notificar instantaneamente via **Telegram**.

## 📋 Funcionalidades

* **Autenticação Híbrida:** Suporte a login manual para resolução de CAPTCHA, com persistência de sessão via cookies serializados (`session_cookies.json`).
* **Monitoramento Inteligente:** Verifica periodicamente a disponibilidade de assentos baseando-se nas classes CSS dos elementos SVG do mapa de assentos.
* **Notificações em Tempo Real:** Integração com a API do Telegram para alertas imediatos com link direto para compra.
* **Resiliência:** Tratamento de expiração de sessão e reconexão automática ao fluxo de verificação.
* **Anti-WAF (Web Application Firewall):** Intervalos de verificação aleatórios (1 a 2 minutos) para mimetizar comportamento humano e evitar bloqueios de IP.

## 🛠️ Tecnologias Utilizadas

* **.NET 10.0** (Console Application)
* **Selenium.WebDriver** & **ChromeDriver** (Automação de Browser)
* **Telegram.Bot** (Integração de Mensageria)
* **DotNetEnv** (Gerenciamento de Variáveis de Ambiente)
* **Newtonsoft.Json** (Manipulação de Cookies)

## 🚀 Como Executar

### Pré-requisitos

1. Tenha o [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) instalado.
2. Google Chrome instalado (o ChromeDriver deve ser compatível com a versão instalada).

### Instalação

1. Clone o repositório:
```bash
git clone https://github.com/seu-usuario/MonitorFiel.git
cd MonitorFiel

```


2. Restaure as dependências do projeto:
```bash
dotnet restore

```



### Configuração (.env)

Crie um arquivo chamado `.env` na raiz do projeto (onde está o `Program.cs`). Utilize o modelo abaixo, substituindo os valores pelos seus dados reais:

```ini
# URL da página de setores do jogo específico (ex: https://www.fieltorcedor.com.br/jogos/...)
MATCH_URL=https://www.fieltorcedor.com.br/jogos/slug-do-jogo/setores/

# Token do seu Bot no Telegram (obtido via @BotFather)
TELEGRAM_BOT_TOKEN=seu_token_aqui

# Seu Chat ID no Telegram (pode ser obtido via bots como @userinfobot)
TELEGRAM_CHAT_ID=seu_chat_id_aqui

```

### Execução

No terminal, execute:

```bash
dotnet run

```

### Fluxo de Primeiro Acesso

Devido aos mecanismos de proteção do site (reCAPTCHA), a primeira execução requer interação humana:

1. O sistema abrirá o navegador Chrome na tela de login.
2. **Faça o login manualmente** e resolva o desafio do CAPTCHA.
3. Após estar logado e ser redirecionado para a home, retorne ao terminal.
4. Pressione **[ENTER]** no console.
5. O sistema salvará os cookies da sessão e iniciará o monitoramento automático. Nas próximas execuções, o login será restaurado automaticamente enquanto os cookies forem válidos.

## ⚠️ Aviso Legal e Ético

Este software foi desenvolvido estritamente para fins educacionais e de uso pessoal para facilitar a compra de ingressos pelo próprio desenvolvedor.

* O uso excessivo de requisições automatizadas pode violar os Termos de Uso da plataforma alvo.
* O autor não se responsabiliza por eventuais bloqueios de conta ou IP decorrentes do uso desta ferramenta.
* Recomenda-se manter os intervalos de verificação (delay) configurados para evitar sobrecarga no servidor do Fiel Torcedor.
