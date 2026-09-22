# ExcelDoc

Aplicação on-premises para importar planilhas e enviar documentos ao SAP Business One. A solução usa ASP.NET Core 10 no backend, Angular 18 no frontend e a SAP Business One Service Layer para autenticação e persistência nas bases SAP HANA.

Não há conexão SQL direta com o HANA. A aplicação não executa migrations: tabelas de usuário, campos e chaves são provisionados pela Service Layer.

## Pré-requisitos de desenvolvimento

- .NET SDK 10
- Node.js compatível com Angular 18
- acesso HTTPS à SAP Business One Service Layer

Restaure as dependências e execute a solução:

```powershell
Set-Location .\ExcelDoc\exceldoc.client
npm ci

Set-Location ..\ExcelDoc.Server
dotnet run
```

O perfil de desenvolvimento inicia o proxy do Angular. As chamadas do frontend usam caminhos relativos `/api`, portanto backend e frontend permanecem na mesma origem.

## Configuração SAP

A configuração por instalação fica na seção `SapServiceLayer`:

```json
{
  "SapServiceLayer": {
    "BaseUrl": "https://sap-interno:50000/b1s/v1/",
    "RequestTimeoutSeconds": 100,
    "Bases": [
      {
        "Database": "SBOPROD_BR",
        "Description": "Base de produção"
      }
    ]
  }
}
```

Regras operacionais:

- `BaseUrl` aponta para a raiz versionada da Service Layer.
- `Database` deve ser exatamente o `CompanyDB` aceito pelo SAP.
- somente `Database` e `Description` são expostos na lista pública da tela de login;
- nomes de bases devem ser únicos, sem espaços nas extremidades;
- credenciais SAP são as do usuário no login e não devem ser gravadas no arquivo de configuração.

As chamadas à Service Layer usam B1SLayer. Cada login mantém uma única
`SLConnection` para a combinação de base e usuário durante toda a sessão; a
biblioteca gerencia os cookies, a renovação da sessão, as tentativas e o
transporte HTTPS.

Segredos de JWT e qualquer outro segredo de produção devem ser fornecidos por configuração protegida do servidor, nunca mantidos com valores padrão no repositório.
O `appsettings.json` de produção não contém `Jwt:SecretKey` de propósito:
defina `Jwt__SecretKey` com um valor aleatório de pelo menos 32 caracteres no
ambiente protegido do servidor. A aplicação falha na inicialização quando o
segredo está ausente, curto ou ainda é um placeholder.

A interface incorpora localmente a fonte de ícones Material Symbols. Depois de
publicado, o site não depende do Google Fonts nem de outro CDN para funcionar.

## Testes

```powershell
dotnet test .\ExcelDoc\ExcelDoc.slnx
```

Para os testes do frontend:

```powershell
Set-Location .\ExcelDoc\exceldoc.client
npm test -- --watch=false
```

Execute `npm audit` explicitamente no pipeline de segurança. O build .NET não
faz auditoria de rede automática, para permanecer reproduzível em redes internas.

## Publicação para IIS

Execute em uma máquina de build com .NET SDK e Node.js:

```powershell
dotnet publish .\ExcelDoc\ExcelDoc.Server\ExcelDoc.Server.csproj `
  --configuration Release `
  --output .\artifacts\iis
```

O publish executa `npm ci`, compila o Angular em modo de produção e copia o bundle para `wwwroot`. Antes de implantar, confirme a presença de:

- `web.config`;
- `ExcelDoc.Server.dll` ou `ExcelDoc.Server.exe`;
- `wwwroot\index.html`;
- arquivos JavaScript e CSS versionados em `wwwroot`.

O servidor IIS precisa apenas do Hosting Bundle do .NET 10; Node.js não é necessário no servidor de produção.

Consulte o [runbook de SAP e IIS](ExcelDoc/docs/SAP-IIS-RUNBOOK.md) para instalação, permissões, TLS, reciclagem e smoke tests.

## Validação de licença

Cada login consulta o DevHub antes de inicializar a base ou emitir o JWT.
O número de instalação é obtido da sessão SAP do próprio usuário. O parceiro e sua
licença são consultados em `BusinessPartners/installation-number/{numero}` e
`licenses/search`, com o ProductId **7**. Somente licença ativa e não vencida libera
acesso, inclusive para administradores. Parceiro/licença ausente resulta em HTTP
403; falhas na consulta resultam em HTTP 503 e bloqueiam o login. A sessão SAP é
encerrada quando o login é recusado. A mensagem aparece na tela de login.

Configure a seção `Licensing` no servidor:

```json
{
  "Licensing": {
    "BaseUrl": "https://devhub.b2finance.com/",
    "ProductId": "7"
  }
}
```

A URL deve usar HTTPS; cada chamada HTTP tem timeout de 30 segundos. Somente o token da API é
armazenado em memória, com renovação antecipada e uma nova tentativa em caso de
HTTP 401. Nenhuma decisão de licença é reutilizada entre logins. Datas de validade
são comparadas como instantes (UTC); datas sem offset seguem o fuso do servidor.
A validação se aplica a novos logins; não revoga JWTs já emitidos.
