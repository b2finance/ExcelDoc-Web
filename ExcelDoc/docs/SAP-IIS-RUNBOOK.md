# Runbook de implantação: SAP Business One e IIS

Este runbook cobre uma instalação on-premises do ExcelDoc. A aplicação deve ser publicada antes de ser copiada ao servidor do cliente.

## 1. Pré-requisitos

Na máquina de build:

- .NET SDK 10;
- Node.js compatível com Angular 18;
- acesso ao registro npm durante `npm ci`.

No servidor:

- Windows Server com IIS;
- recurso IIS **Application Initialization**, para `preloadEnabled`;
- Hosting Bundle do .NET 10 instalado depois do IIS;
- certificado HTTPS para o site;
- conectividade do servidor web até a SAP Business One Service Layer;
- cadeia da autoridade certificadora da Service Layer confiável no Windows.

O Hosting Bundle instala o runtime e o ASP.NET Core Module. Se ele tiver sido instalado antes do IIS, repare a instalação do bundle.

Referências:

- [Hospedar ASP.NET Core no IIS](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
- [SAP Business One Service Layer](https://help.sap.com/docs/SAP_BUSINESS_ONE/f110a154dd0f4c20bf7f3ebca9eeb794/60c7a0b745bd486589f05a1da77041f3.html)

## 2. Configurar a instalação

Configure `DevHub:BaseUrl` apenas quando necessário para outro ambiente HTTPS. O DevHub fornece as bases e a URL da Service Layer para o cliente autorizado. O usuário informa suas credenciais SAP ao conectar. Configure o produto em `Licensing:ProductId`. Configure `SapServiceLayer:RequestTimeoutSeconds` e `SapServiceLayer:Language` conforme a instalação.

Não adicione:

- connection string do HANA;
- usuário ou senha SAP;
- certificado ignorado em produção;
- segredos com os valores de exemplo do repositório.

Use HTTPS para a URL da Service Layer. O transporte, a sessão e as renovações de autenticação são gerenciados pelo B1SLayer; não configure um `HttpClient` paralelo para essas chamadas.

Defina também `Jwt__SecretKey` como segredo protegido da instalação, com pelo
menos 32 caracteres aleatórios. O valor não está no `appsettings.json` de
produção e a aplicação se recusa a iniciar sem ele. Não reutilize a chave entre
clientes ou ambientes.

## 3. Gerar e conferir o artefato

Na raiz do repositório:

```powershell
dotnet publish .\ExcelDoc\ExcelDoc.Server\ExcelDoc.Server.csproj `
  --configuration Release `
  --output .\artifacts\iis
```

O target de publish:

1. executa `npm ci` usando `package-lock.json`;
2. executa o build Angular de produção;
3. inclui `dist\exceldoc.client\browser` em `wwwroot`.

Falhe a entrega se `web.config` ou `wwwroot\index.html` não existirem. Abra o `index.html` e confirme que os arquivos JS/CSS referenciados também estão no artefato.

## 4. Criar o site no IIS

1. Copie o artefato para uma pasta versionada, por exemplo `D:\Apps\ExcelDoc\releases\<versao>`.
2. Crie um application pool exclusivo com:
   - `.NET CLR Version`: `No Managed Code`;
   - pipeline integrado;
   - 64 bits habilitado;
   - identidade `ApplicationPoolIdentity`.
3. Crie o site apontando para a pasta publicada.
4. Adicione binding HTTPS e o certificado do site.
5. Implante como raiz do site. O frontend usa `<base href="/">` e URLs `/api`; uma subaplicação exige ajuste explícito do base path.
6. Conceda leitura e execução à identidade `IIS AppPool\<NomeDoPool>` na pasta publicada.

O Web SDK gera `web.config` durante o publish. Ele deve permanecer na raiz física do site.

## 5. Arquivos enviados e reciclagem

O diretório de uploads precisa:

- ficar fora da pasta versionada de publish;
- usar caminho absoluto na configuração de produção;
- conceder `Modify` apenas à identidade do application pool;
- ser tratado como armazenamento temporário, sem necessidade de backup.

O ExcelDoc mantém cada planilha somente enquanto o processamento está na fila,
em execução ou aguardando uma nova tentativa. Ao concluir o job, com sucesso ou
erro, o arquivo é removido. Falhas anteriores ao enfileiramento também removem o
upload já gravado.

A sessão SAP e a fila de trabalho atuais são mantidas em memória do processo. Uma reciclagem:

- encerra sessões armazenadas;
- perde itens que ainda estavam somente na fila;
- pode interromper um processamento em andamento.

Enquanto não houver fila e sessão duráveis, configure:

- application pool `startMode=AlwaysRunning`;
- aplicação `preloadEnabled=true`;
- idle timeout desabilitado;
- uma única instância do worker;
- janela de reciclagem controlada.

Essas opções reduzem interrupções, mas não substituem persistência durável.

Quando há um processamento enfileirado, o ExcelDoc mantém uma referência interna
à sessão SAP até o término do job. Se o usuário sair durante esse período, novas
requisições são bloqueadas e o logout na Service Layer é concluído ao liberar o
último job.

## 6. Rede e autorizações SAP

Libere somente o tráfego necessário do servidor IIS para o host/porta HTTPS da Service Layer. Não exponha o HANA diretamente.

O provisionamento de UDTs/UDFs e chaves ocorre via Service Layer e requer um superusuário SAP autorizado a alterar metadados. A função de administrador do ExcelDoc continua restrita a `manager` e `Support` e não amplia permissões no SAP.

No primeiro login de um superusuário SAP, o sistema cria as UDTs `bott_MasterData`,
registra os UDOs `boud_MasterData`, adiciona UDFs/chaves únicas e grava a versão
do esquema. Os dados padrão são inseridos apenas quando ausentes e não são
sobrescritos nos logins seguintes.

Esta versão pressupõe um provisionamento limpo. Se uma base de homologação
recebeu metadados de uma build anterior, remova os UDOs, UDTs e UDFs dessa build
pelas ferramentas administrativas do SAP antes do primeiro login na versão 2.
Não remova metadados diretamente por SQL: o provisionador atual é aditivo e não
executa migrations destrutivas.

Uma sessão válida precisa preservar `B1SESSION` e `ROUTEID`. O logout da aplicação deve encerrar a sessão na Service Layer. O timeout do JWT não deve deixar uma sessão da aplicação utilizável depois que a sessão SAP expirar.

## 7. Smoke test

Após iniciar o site:

1. acesse a raiz e confirme que a tela de login e seus assets carregam sem 404;
2. entre no DevHub e confirme que apenas os clientes ativos autorizados aparecem;
3. selecione um cliente e uma base, informe a senha SAP e confirme a conexão;
4. confirme `manager` e `Support` como administradores;
5. confirme um usuário comum sem acesso às ações administrativas e sem alteração de modelos/mapeamentos padrão;
6. envie uma planilha pequena e acompanhe o processamento até o SAP;
7. confirme logs sem senhas, payload de login ou cookies SAP;
8. recicle o pool em ambiente de homologação e valide o comportamento documentado para sessão e fila.

## 8. Rollback

Mantenha a versão anterior em outra pasta e altere o caminho físico do site para reverter os binários. Preserve o `appsettings.json` específico do cliente.

Rollback do site não desfaz metadados ou dados já criados no SAP. Antes de atualizar o esquema de UDTs, faça backup da base SAP e registre a versão do esquema aplicada.

## 9. Diagnóstico rápido

- `500.30` ou `502.5`: confirme Hosting Bundle, arquitetura do pool e Event Viewer.
- raiz ou rota Angular retorna 404: verifique `wwwroot\index.html` e a publicação do frontend.
- erro de certificado SAP: instale a cadeia da CA; não habilite certificado inválido.
- `401 Invalid session`: a sessão SAP expirou ou foi perdida em reciclagem; force novo login.
- `403` ao provisionar ou gravar: confira autorizações do usuário no SAP Business One.
- timeout: teste DNS, firewall, porta da Service Layer e `RequestTimeoutSeconds`.
