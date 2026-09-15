# MonitorFiel

O **MonitorFiel** é uma solução de automação desenvolvida em .NET (C#) para monitorar a disponibilidade de ingressos no programa Fiel Torcedor do Sport Club Corinthians Paulista.

O sistema utiliza **Selenium WebDriver** para manter uma sessão autenticada no site, monitorar a liberação da compra e acompanhar os setores **Norte** e **Sul** da Neo Química Arena. Quando encontra disponibilidade, envia alerta pelo **Telegram** e também dispara um **alarme local insistente no computador**.

## 📋 Funcionalidades

* **Autenticação Híbrida:** suporte a login manual para resolução de CAPTCHA, com persistência de sessão via cookies serializados (`session_cookies.json`) e perfil persistente do Chrome.
* **Categoria fixada:** enquanto o card estiver em `Esgotado`, o monitor permanece na rota `/categoria/` e impede o redirecionamento client-side para `/jogos` quando o Chrome/CDP suportar o congelamento de scripts.
* **Detecção de COMPRAR:** identifica quando o card passa de `Esgotado` para `COMPRAR` e tenta abrir `/setores/` imediatamente.
* **Sessão de setores persistente:** depois que `/setores/` é liberado para a sessão atual, o monitor permanece no estádio mesmo que o card da categoria volte a ficar esgotado, até a sessão perder esse acesso.
* **Monitoramento Norte/Sul:** verifica os elementos dos setores `norte` e `sul` e detecta quando deixam o estado `disabled`.
* **Telegram + alarme local:** envia a mensagem de alerta e toca um padrão sonoro insistente por aproximadamente 30 segundos no próprio computador.
* **Deduplicação de alertas:** um setor que permanece aberto não gera spam a cada atualização; um novo alerta é disparado quando ele fecha e volta a abrir.
* **Cadência com jitter:** por padrão, atualiza a categoria a cada **8–12 segundos** e o estádio a cada **6–9 segundos**.
* **Backoff de segurança:** se a página apresentar sinais típicos de rate limit ou bloqueio, o monitor pausa automaticamente por **60–120 segundos** antes de continuar.
* **Resiliência de sessão:** ao perder autenticação/acesso, tenta restaurar a sessão e retorna ao fluxo correto.

> Não existe intervalo que garanta ausência de bloqueio. Limites e mecanismos de proteção podem mudar sem aviso. Os intervalos atuais são uma configuração conservadora do projeto e o backoff existe para reduzir automaticamente a frequência quando houver sinais de restrição.

## 🛠️ Tecnologias Utilizadas

* **.NET 10.0** (Console Application)
* **Selenium.WebDriver** & **ChromeDriver** (automação de navegador)
* **Chrome DevTools Protocol (CDP)** através do Selenium
* **Telegram.Bot** (mensageria)
* **DotNetEnv** (variáveis de ambiente)
* **Newtonsoft.Json** (persistência de cookies)

## 🚀 Como Executar

### Pré-requisitos

1. Tenha o [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) instalado.
2. Google Chrome instalado (o ChromeDriver deve ser compatível com a versão instalada).
3. Para o alarme local, mantenha o volume do Windows/dispositivo de áudio em um nível audível.

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
# URL da tela de setores do jogo específico
MATCH_URL=https://www.fieltorcedor.com.br/jogos/slug-do-jogo/setores/

# Token do seu Bot no Telegram (obtido via @BotFather)
TELEGRAM_BOT_TOKEN=seu_token_aqui

# Seu Chat ID no Telegram
TELEGRAM_CHAT_ID=seu_chat_id_aqui
```

A URL da categoria é derivada automaticamente do `MATCH_URL`. Por exemplo:

```text
MATCH_URL:     /jogos/corinthians-x-rosario-cl26/setores/
CATEGORIA_URL: /jogos/corinthians-x-rosario-cl26/categoria/
```

### Execução

No terminal, execute:

```bash
dotnet run
```

### Fluxo de monitoramento

1. O programa restaura a sessão ou solicita login manual.
2. Tenta abrir `/setores/` para descobrir se a sessão já possui acesso ao estádio.
3. Se ainda não possui acesso, entra e permanece na página `/categoria/`.
4. Enquanto estiver `Esgotado`, atualiza a categoria periodicamente.
5. Quando aparece `COMPRAR`, tenta acessar `/setores/` imediatamente.
6. Depois de entrar no estádio, permanece atualizando `/setores/` e verificando Norte/Sul.
7. Quando Norte ou Sul abre, envia Telegram e dispara o alarme local.
8. Se a sessão perder o acesso, volta ao fluxo de categoria e aguarda uma nova liberação de `COMPRAR`.

### Fluxo de Primeiro Acesso

Devido aos mecanismos de proteção do site (reCAPTCHA), a primeira execução pode requerer interação humana:

1. O sistema abrirá o navegador Chrome na tela de login.
2. **Faça o login manualmente** e resolva o desafio do CAPTCHA, se aparecer.
3. Aguarde estar realmente autenticado.
4. Retorne ao terminal e pressione **[ENTER]**.
5. O sistema valida a sessão, salva os cookies e inicia o monitoramento automático.

## ⚠️ Aviso Legal e Ético

Este software foi desenvolvido para fins educacionais e de uso pessoal.

* Automação e atualizações frequentes podem estar sujeitas aos Termos de Uso da plataforma alvo.
* Não há garantia de que uma determinada frequência de atualização não gere limitação, CAPTCHA ou bloqueio.
* O projeto reduz automaticamente a frequência quando identifica sinais conhecidos de rate limit/bloqueio.
* Evite aumentar manualmente a frequência sem conhecer os limites permitidos pela plataforma.
