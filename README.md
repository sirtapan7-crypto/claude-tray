# Claude Code Tray — C# / .NET

Reescrita nativa (WinForms `NotifyIcon` + GDI+) do monitor de uso do **Claude Code**
que vive **somente na bandeja (tray) do Windows** e mostra a **porcentagem de uso**.

Por que .NET em vez do Python: o número é desenhado como **vetor** (`GraphicsPath`,
com contorno), **no tamanho real** que a tray pede (`SM_CXSMICON`) e com **DPI awareness**
(`PerMonitorV2`). Nada de reduzir um bitmap de 64px — o número fica nítido, sobretudo em
telas 125–200% (ícones de 20–32px).

## Visual

- Fundo: clay/coral do Claude `#D97757`
- **Barra de preenchimento vertical em azul** (estilo Gerenciador de Tarefas) sobe de
  baixo pra cima, proporcional ao uso (50% = metade de baixo azul; 100% = tile todo azul)
- **Borda 3D (chanfro)**: realce claro no topo/esquerda e sombra embaixo/direita → relevo
- Número: dígitos grandes, brancos com **contorno escuro** (legível em qualquer tamanho)
- ≥90%: o fundo pisca
- Âmbar = erro de API · cinza = conectando

Tooltip (passar o mouse): sessão 5h, semana 7d, uso extra, contagem até o reset e status.

## Fonte dos dados

Uma chamada mínima à API da Anthropic (Haiku, 1 token) a cada 5 min lê os headers
`anthropic-ratelimit-unified-*`, usando o token OAuth que o Claude Code mantém em
`~/.claude/.credentials.json`. Nenhuma configuração extra.

## Requisitos

- Windows 10/11
- .NET 10 SDK (para compilar) — o `.exe` self-contained não exige .NET instalado para rodar
- Claude Code instalado e logado (rode `claude` ao menos uma vez)

## Compilar e rodar

```
dotnet run -c Release            # compila e executa
```

### Gerar um .exe único (self-contained, sem dependências)

```
dotnet publish -c Release
```

O executável sai em `bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe`.
Pode ser copiado para qualquer lugar e roda sem .NET instalado.

### Iniciar com o Windows

Três formas, da mais simples à mais completa:

1. **Pelo menu do app** (recomendado): botão direito no ícone → **Start with Windows**.
   Grava/remove uma chave em `HKCU\…\Run` apontando para o `.exe` atual. Sem admin.
2. **Instalador** (veja abaixo): marque "Iniciar com o Windows" durante a instalação.
3. **Manual**: `Win + R` → `shell:startup` → crie um atalho para o `ClaudeTray.exe`.

### Gerar o instalador (Inno Setup)

Requer o [Inno Setup 6](https://jrsoftware.org/isdl.php).

```
dotnet publish -c Release
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
```

Gera `dist\ClaudeTray-Setup.exe` — instalação per-user (sem admin) em
`%LocalAppData%\ClaudeTray`, com atalho no Menu Iniciar, opção de autostart e
desinstalador. O script é o [installer.iss](installer.iss).

## Menu (botão direito no ícone)

- **Show on icon** — Session 5h / Week 7d / Extra
- **Refresh now** — leitura imediata da API
- **Quit**

## Estrutura

| Arquivo | Responsabilidade |
|---|---|
| `Program.cs` | entrada, `ApplicationContext`, tray icon, menu, timers de poll/flash |
| `ApiClient.cs` | lê credenciais, chama a API, parseia os headers de rate-limit |
| `IconRenderer.cs` | desenha o ícone com GDI+ (vetor + contorno) no tamanho real |

> Dica de dev: `dotnet run -- --render <dir>` despeja PNGs de exemplo nos tamanhos
> 16/20/32 px para inspeção visual.

## Solução de problemas

- **Ícone cinza** → ainda conectando; aguarde a primeira chamada.
- **Ícone âmbar / tooltip "API error"** → token pode ter expirado. Rode `claude` no terminal.
