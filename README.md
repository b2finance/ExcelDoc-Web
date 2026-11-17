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

## Autenticação DevHub e conexão SAP

O usuário entra com suas credenciais DevHub e escolhe um cliente ativo autorizado. O servidor consulta as bases e a configuração da Service Layer desse cliente, valida a associação dos dados e solicita uma senha SAP para a base escolhida. A sessão do ExcelDoc só é emitida após a conexão SAP, a validação de licença e a inicialização da base.

O token DevHub permanece no servidor e é enviado como Bearer apenas às consultas DevHub. Sua expiração é verificada antes de novas consultas ou conexões; a expiração não interrompe uma conexão SAP já ativa. Ao trocar de cliente ou conexão, informe novamente a senha SAP. A troca invalida a sessão anterior e cancela as requisições pendentes; operações já recebidas pelo SAP podem ter sido concluídas antes do cancelamento.

Por padrão, o DevHub usa `https://devhub.b2finance.com/`. Para outros ambientes, configure `DevHub:BaseUrl` como uma URL HTTPS. `SapServiceLayer:RequestTimeoutSeconds` e `SapServiceLayer:Language` continuam configuráveis no servidor. As bases, o usuário SAP e a URL da Service Layer vêm do DevHub, não da tela nem da configuração local.

O perfil de administrador do ExcelDoc continua associado aos usuários SAP `manager` e `Support`. As permissões do DevHub são consultadas no perfil, mas não há no contrato um mapeamento de menus DevHub para papéis do ExcelDoc.

As sessões ficam em memória no servidor e são perdidas ao reiniciá-lo. O acesso deve ser feito por HTTPS; os segredos de configuração devem ficar em um armazenamento protegido. O segredo JWT precisa ter pelo menos 32 caracteres e deve ser fornecido em `Jwt__SecretKey`.

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
`licenses/search`, com o produto 13. Somente licença ativa e não vencida libera
acesso, inclusive para administradores. Parceiro/licença ausente resulta em HTTP
403; falhas na consulta resultam em HTTP 503 e bloqueiam o login. A sessão SAP é
encerrada quando o login é recusado. A mensagem aparece na tela de login.

Configure a seção `Licensing` no servidor e forneça `Licensing__UserName` e `Licensing__Password` por configuração protegida. As credenciais de licenciamento são independentes da senha informada pelo usuário na tela:

```json
{
  "Licensing": {
    "BaseUrl": "https://devhub.b2finance.com/",
    "ProductId": "13"
  }
}
```

A URL deve usar HTTPS; cada chamada HTTP tem timeout de 30 segundos. Somente o token da API é
armazenado em memória, com renovação antecipada e uma nova tentativa em caso de
HTTP 401. Nenhuma decisão de licença é reutilizada entre logins. Datas de validade
são comparadas como instantes (UTC); datas sem offset seguem o fuso do servidor.
A validação se aplica a novos logins; não revoga JWTs já emitidos.

## Métricas de documentos processados

Após a inserção no SAP e a gravação do resultado de sucesso, cada documento gera
um `POST /logs` autenticado no DevHub. O corpo inclui mensagem, data UTC, nível
`Trace` (0), parceiro e serial da licença validada no login. O parceiro e o serial
pertencem à sessão SAP e não são compartilhados entre clientes. A mensagem contém
base, processamento, IdExcel e identificador único; não envia o conteúdo
da planilha nem o payload do documento.

Erros de validação, falhas de inserção e documentos ignorados por duplicidade não
geram logs externos. Falhas no envio da métrica são registradas apenas localmente:
o documento mantém seu sucesso e o lote continua. Não há reenvio automático nem
fila durável de métricas; indisponibilidade do DevHub pode resultar em perda da
métrica. O POST não é repetido após falha para evitar duplicação quando a resposta
se perde. O token da API usa a mesma renovação por expiração do licenciamento.
