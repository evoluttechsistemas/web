# AtendimentoIA — EvolutTech IA

Aplicação administrativa Blazor Web App (.NET 8, Interactive Server) para Windows/IIS. O projeto existente foi aproveitado; o template inicial foi preservado no diretório de trabalho do chat antes das alterações.

## Funcionalidades entregues

- Visão geral com métricas reais, fila de indexação, cancelamento e histórico persistido.
- Fonte GitHub configurável: repositório, branch/tag/commit e credencial de leitura protegida. Repositório inicial: `evoluttechsistemas/sistema`.
- Resolução para commit imutável, download seguro e análise Roslyn sem executar código do ERP, MSBuild, hooks, geradores ou SQL encontrado nos arquivos.
- Consulta de Forms, arquivos relativos, linhas, eventos e diagnósticos. As mesmas limitações de análise semântica da etapa 1 se aplicam quando faltam referências.
- Cadastro de orientações por segmento e módulo, com autoria, data, indicação de público e arquivamento.
- Conexão SQL protegida e leitura dos catálogos: objetos, colunas, índices e relacionamentos.
- Consulta por código de chamado: segmento, maior versão das máquinas do cliente em `ControleMensalista.Versao` e histórico em páginas de até 40 anotações (máximo 6.000 caracteres por anotação). A comparação de versões usa componentes numéricos (`26.10` maior que `26.9`), filtra cliente e empresa e ignora valores vazios ou fora do formato numérico com pontos. Não usa a versão antiga registrada no chamado como substituição.

O simulador em `/simulador` faz chamadas reais à OpenAI quando uma chave válida está configurada e há fontes relevantes. Oferece modos Cliente/suporte, Técnico e Cliente/comercial, separando rascunho, fontes e análise interna. Não há resposta de modelo fictícia quando falta chave ou evidência. A marcação "permitido ao cliente" permite recuperar a orientação para os modos externos; não publica nem envia mensagens a clientes. Geração de documentação, busca vetorial/embeddings e automação por webhook permanecem para etapas futuras.

## Fontes e escopo

O ERP será lido exclusivamente do GitHub. Não há caminho para a pasta local do ERP no aplicativo. A execução nesta máquina serve para desenvolver e testar o painel; a instalação final e os dados de execução ficarão no servidor.

Foi confirmado no banco remoto que existem:

- `dbo.TicketChamadoC`: chamado, identificador `Codigo`, cliente em `CodCliente`, empresa em `CodEmp`, versão em `Versao`.
- `dbo.Cliente`: `Codigo`, `Segmento` e `CodEmp`. A consulta exige a mesma empresa nos dois lados do vínculo.
- `dbo.ControleMensalista`: versão de cada máquina em `Versao`, vinculada por `CodCliente` e `CodEmp`. O contexto utiliza a maior versão numérica entre as máquinas do mesmo cliente e empresa.
- `dbo.ParametroDinamico`: catálogo de parametrizações. A recuperação seleciona somente identificador, nome, grupo e descrição; nunca seleciona `Valor` nem `ValorPadrao`. A definição indica uma possibilidade configurável, não uma configuração habilitada no cliente. Definições e trechos relevantes de código podem ser comparados na resposta, sem presumir um vínculo que o código não demonstre.
- `dbo.TicketChamadoD`: anotações em `Anotacao`, vínculo em `CodTicketChamadoC` e empresa em `CodEmp`.
- `dbo.TicketAssistenteIA`: mensagens e fontes de interações anteriores. Conteúdo histórico de IA exige validação, não é tratado automaticamente como verdade.
- `dbo.DocumentacaoTutorial` e `dbo.DocumentacaoTutorialBloco`: documentação produzida pela equipe, com campos de publicação/visibilidade a interpretar antes de qualquer divulgação.
- `dbo.DocumentacaoAutomatica` e `dbo.DocumentacaoAutomaticaTicket`: material e vínculos já existentes.
- Tabelas de backup também aparecem no catálogo; não são automaticamente fontes válidas de conteúdo.

O simulador pode consultar conteúdo relevante de `DocumentacaoTutorial`, `DocumentacaoTutorialBloco`, `ParametroDinamico` e orientações cadastradas, limitado pela empresa e pelo modo selecionado. Históricos são opcionais: no modo Técnico, ficam restritos ao cliente do chamado (`TicketChamadoD` e `TicketAssistenteIA`); no comercial, apenas ao card informado (`CRMAnotacao`, vinculado por `CodCRMC → CRMC.Codigo`). Não há pesquisa de anotações de todos os clientes ou leads. Material histórico, especialmente respostas antigas de IA, não é prova de uma funcionalidade. Tabelas de backup e embeddings legados não são lidos pela recuperação inicial. O significado dessas fontes foi fornecido pela equipe e a existência das colunas foi verificada nos catálogos.

Chamados, histórico, base do assistente e documentação do suporte são fontes internas. A semelhança com a estrutura dos bancos dos clientes não concede acesso externo a esses registros. Todas as telas são administrativas. Os campos pessoais do cadastro de clientes (nome, documentos, telefones, senhas etc.) não fazem parte da consulta de contexto. O texto histórico pode conter informações sensíveis: a máscara aplicada é apenas uma redução de exposição no painel interno e não substitui revisão antes de qualquer integração externa.

`Support:CompanyId` foi configurado como `2`, conforme o exemplo fornecido. As consultas usam parâmetros SQL, exigem empresa compatível e não aceitam scripts SQL arbitrários. Não alteramos permissões nem registros do banco. As permissões da conta de origem devem ser administradas no servidor; o fato de o aplicativo emitir apenas SELECT não transforma uma conta com escrita em uma conta somente leitura.

## Executar para desenvolvimento

Na pasta do projeto:

```powershell
dotnet build AtendimentoIA.sln
dotnet run --project AtendimentoIA.csproj --launch-profile http
```

URL: `http://localhost:5214`. O login do HELP é exigido também em desenvolvimento. Configure a conexão antes do primeiro acesso; os dados iniciais não são simulados: sem indexação, os contadores mostram `—`.

O SDK é fixado pelo `global.json` em 8.0.401 com atualização de patch permitida. Foram reaproveitados os assemblies de análise em `src/EvolutTech.IA.Core` e `src/EvolutTech.IA.Indexador`; o Console continua disponível.

## Testar respostas da IA

1. Se o Visual Studio estiver executando a aplicação, pare com Shift+F5, recompile e execute com F5. Durante a implementação, a validação compilou para uma saída isolada para não bloquear a DLL da sua execução.
2. Abra a tela única `/configuracoes`, seção OpenAI (`#ia`), cadastre uma chave da API OpenAI e mantenha um modelo compatível com Responses API e JSON Schema. GitHub, banco e OpenAI ficam nessa mesma página, com botões de salvar independentes. Campos de senha vazios preservam as credenciais existentes. Os links antigos `/fontes` e `/configuracao-ia` redirecionam para essa tela. O modelo inicial é `gpt-4.1-mini`; o acesso depende do projeto da sua chave. A chave é diferente do token do GitHub e fica protegida como os demais segredos.
3. Abra `/simulador`. Escolha Cliente/suporte, Técnico ou Cliente/comercial. Informe opcionalmente o chamado ou card CRM e digite uma pergunta. Sem vínculo, é possível perguntar sobre funcionalidades gerais, informando um segmento para teste.
4. Para usar dados históricos, marque a opção correspondente. No modo Técnico, recupera anotações do cliente do chamado e interações anteriores de IA; no comercial, apenas anotações do card. Históricos do suporte não entram na resposta externa de Cliente/suporte.
5. Confira o rascunho, fontes, versão e limitações. A análise técnica e os localizadores de fontes são exclusivamente administrativos. A mudança de cliente, card, segmento ou modo limpa o contexto da conversa no próximo envio.

O sistema usa recuperação textual inicial, com até 12 fontes e trechos limitados, sem enviar todo o repositório a cada pergunta. A consulta acompanha as perguntas anteriores da mesma conversa, sem usá-las como prova factual. Os trechos de código são conferidos por SHA-256 antes do envio; o commit não é presumido como equivalente à versão instalada no cliente. Não há busca vetorial nem mapeamento automático entre versões de release e commits nesta entrega.

Orientações só entram nos modos de cliente/comercial quando marcadas como permitidas ao cliente. A documentação SQL é interna por padrão: para liberar documentos já publicados aos modos externos, configure `AI:DocumentPublishedValue` e `AI:PublicDocumentVisibility` com os códigos reais do seu sistema, após conferir seu significado. Sem mapeamento explícito de visibilidade, os documentos SQL aparecem apenas no modo Técnico. As definições de parâmetros e os fatos do código podem sustentar uma explicação, mas não comprovam que algo esteja habilitado no ambiente do cliente.

Somente pergunta, contexto mínimo e trechos filtrados são enviados para `https://api.openai.com/v1/responses`, com `store=false`. Identificadores de chamado/cliente/card, localizadores de arquivos, nomes de usuários e strings de conexão não integram a estrutura do contexto remoto. Textos livres passam por máscaras, que não garantem a detecção de todos os dados pessoais. Não insira dados sensíveis em perguntas desnecessariamente.

O chat fica na memória da sessão do navegador. O histórico de simulações registra apenas metadados em `App_Data/simulation-audit` (horário, modo, referências e contagem de tokens), sem gravar perguntas ou respostas completas. Nenhuma mensagem vai ao WhatsApp ou ao EvolutHelp e nenhuma simulação grava no SQL Server.

Sem chave, o simulador explica a configuração pendente. Sem fontes, retorna uma informação de insuficiência local, explicitamente sem chamada ao modelo. Erros de autenticação, acesso ao modelo, quota e geração incompleta são exibidos sem expor o corpo bruto da API. Uma fonte histórica ou citação inventada não valida uma afirmação de funcionalidade.

## Persistência e segredos (detalhes)

Nesta versão inicial, orientações, execuções, relatórios e o snapshot do catálogo são gravados em `App_Data`, fora de `wwwroot` e excluído do Git e da publicação. Isso é persistência real em arquivos, sem criar tabelas no banco de origem. A migração dos dados de conhecimento para o banco separado `EvolutTechIA` é uma etapa posterior.

Tokens e conexão SQL são criptografados com ASP.NET Core Data Protection. As chaves persistidas são protegidas pelo DPAPI do Windows, vinculadas à identidade que executa o aplicativo. O painel não revela valores já cadastrados. O token também pode ser fornecido pela variável de ambiente `GitHub__Token`; nunca o grave em arquivos versionados.

A conexão fornecida no chat já foi cadastrada com proteção para o usuário de desenvolvimento. **Não copie esse segredo para o servidor esperando que seja descriptografado por outro usuário.** No IIS, cadastre novamente as credenciais sob a identidade do aplicativo. Não versione, publique como arquivo público ou compartilhe `App_Data`, chaves e backups.

Administração por terminal, sem senha em argumentos:

```powershell
dotnet bin/Debug/net8.0/AtendimentoIA.dll --configure-sql true
```

O comando lê uma linha com a conexão sem ecoar a entrada. Para atualizar apenas o catálogo (SELECT de metadados):

```powershell
dotnet bin/Debug/net8.0/AtendimentoIA.dll --inspect-sql true
```

## Publicar no IIS

1. Instale o Hosting Bundle .NET 8 e suporte a WebSockets no Windows/IIS.
2. Publique `AtendimentoIA.csproj` com `dotnet publish AtendimentoIA.csproj -c Release -o <pasta-de-publicacao>` e configure o site na raiz do hostname dedicado.
3. Use HTTPS, habilite **Autenticação Anônima** no site e desabilite **Autenticação do Windows**. O AtendimentoIA autentica pelo formulário `/login` e protege o painel com cookie HttpOnly; `Panel:AdminUsers` e `Panel:AllowLocalDevelopment` não são mais usados. O desenvolvimento também passa pelo login.
4. Configure `AllowedHosts` para o hostname real. Use `appsettings.Production.example.json` como modelo de `appsettings.Production.json` no servidor (não sobrescreva valores já existentes). Antes do primeiro login, configure a conexão do banco em `ConnectionStrings:Connection` ou `ConnectionStrings__Connection`. O arquivo de exemplo não contém credenciais. A conexão protegida cadastrada no painel tem prioridade; a configuração serve ao primeiro acesso, sem depender de autenticação para cadastrar o próprio banco de login.
5. Configure `Panel__DataDirectory` para uma pasta privada, persistente e fora da pasta publicada. Dê acesso somente à identidade do pool e aos administradores responsáveis; habilite o carregamento do perfil de usuário do pool para DPAPI.
6. Entre com um usuário ativo de `Usuario`, `Help='S'` e `CodEmp=2`, conforme definido pelo responsável. A validação usa BCrypt quando `SenhaHash` está preenchido e a senha legada exata somente quando não há hash, respeitando o bloqueio do HELP. Não seleciona outra empresa pelo navegador. Cadastre novamente os segredos pelo painel autenticado sob a identidade do pool; não copie chaves DPAPI de outro usuário/máquina. Restrinja o token GitHub a Contents: Read do repositório necessário.
7. Instale o targeting pack .NET Framework 4.8 ou indique `Indexing__ReferenceDirectory` com assemblies de referência. Sem eles, a análise sintática funciona e as limitações semânticas são registradas.

Por solicitação do responsável, o AtendimentoIA autentica somente com usuário e senha, sem confirmação de e-mail, código de verificação ou configuração SMTP. Os campos `Requer2FA` e `EmailVerificado` do HELP não são usados no login deste painel e não são alterados no banco. Não há cadastro, recuperação ou migração de senha nesta aplicação.

O login faz somente SELECT no banco: não altera senha, hash, tentativas, status online, sessões ou último acesso do HELP. Há limite de dez solicitações de login por IP por minuto e proteção antifalsificação nos formulários. A sessão dura até oito horas, é revista no banco em requisições e a cada cinco minutos nos circuitos Blazor; inativação, remoção do acesso HELP ou mudança da data de senha invalidam o acesso. Senhas, hashes e conexão não vão para localStorage ou para o cookie de sessão. Todos os usuários ativos do HELP da empresa 2 têm acesso ao painel administrativo, por decisão do responsável nesta versão.

O worker manual atual é um `BackgroundService` hospedado no mesmo processo do painel: fechar o navegador não cancela a indexação. Reiniciar/reciclar o IIS interrompe a execução; na inicialização seguinte, o histórico registra a interrupção e permite iniciar de novo. Para automação e execução contínua independente de reciclagens, a próxima etapa deve extrair a fila para um Worker/Windows Service com armazenamento de jobs. Este aplicativo ainda não é um agendador durável nem suporta múltiplas instâncias escrevendo na mesma pasta.

## Acompanhamento da indexação

O painel exibe a etapa atual (resolução do commit, download, extração, leitura C#, análise semântica, mapeamento e gravação), contadores reais, tempo decorrido e horário do último progresso. O download mostra bytes recebidos; quando o GitHub não informa o tamanho total, a barra é indeterminada. A porcentagem, quando disponível, refere-se apenas à etapa atual. Uma etapa de análise semântica pode permanecer um período sem alterar o contador; isso não comprova travamento.

O estado de progresso é persistido aproximadamente a cada dois segundos. Não se deve parar/reiniciar o processo para aplicar uma atualização enquanto uma indexação estiver em execução: isso interrompe o trabalho. Nesta versão, a execução interrompida não é retomada automaticamente e a indexação ainda baixa e analisa a árvore inteira.

Na medição do repositório informado, o arquivo de download tem cerca de 239 MB e inclui aproximadamente 63 MB de C# e 168 MB de `.resx`. A extração agora ignora `.resx` por padrão porque o analisador atual não os processa. Para habilitá-los em uma etapa futura, configure `Indexing:IncludeResources=true`. A exclusão reduz a extração, mas não reduz o tamanho do ZIP obtido do GitHub. `.Designer.cs` continua incluído.

## Módulos e parâmetros nas respostas

### Base da aplicação e descoberta por conteúdo

A base confirmada pelo responsável é `Principal/Aplicacoes/Menu`. O projeto principal fica persistido em `knowledge-settings.json`, `ModuleProjects.Aplicacao = Principal`; consultas gerais usam essa origem e as consultas com módulo explícito preservam sua seleção própria.

A descoberta agora pesquisa o conteúdo de todos os arquivos C# verificados do projeto antes de limitar a lista para análise sintática. O catálogo inclui os textos de recursos usados pelo Designer, é mantido em memória por snapshot de análise e não executa o código do ERP. Os menus com correspondência no conteúdo recebem prioridade, ainda que o nome do arquivo não contenha o assunto. A seleção final de trechos continua limitada; a limitação acontece após a descoberta por conteúdo. As leituras de evidências verificam novamente os hashes. Uma pergunta nova não herda temas anteriores; apenas esclarecimentos e referências explícitas como “isso” reaproveitam o assunto da conversa.

Termos plurais regulares como “perdas” são associados a “perda”, e verbos genéricos de configuração não substituem o assunto. Na consulta SQL de parâmetros, a correspondência no grupo tem prioridade. Definições encontradas apenas pelo grupo também entram na evidência; são eliminadas as duplicações de nome/grupo entre código e banco.

Quando a resposta cita uma ação de menu, a apresentação completa a hierarquia efetivamente declarada no Designer. Perguntas de configuração incluem o checklist das definições pertinentes do grupo encontradas nas fontes, com seus nomes e descrições, para avaliação pelo suporte. Isso não consulta nem presume os valores do cliente. No caso conferido: `Estoque → Perdas → Lançamento de Perdas`; grupo `PERDA`, com `CODIGO_FORNECEDOR_PADRAO`, `CODIGO_PLANO_CONTAS_PADRAO` e `CODIGO_CENTRO_CUSTOS_PADRAO`.

Os eventos do Menu são ligados às classes de telas que instanciam, dentro do mesmo projeto. Quando a pergunta corresponde a um ramo do menu, a seleção da interface fica limitada às telas abertas por esse ramo; em configuração, são priorizadas as telas que declaram parâmetros do assunto. Isso evita, por exemplo, misturar o módulo de Perdas com um campo de percentual em outra operação. A complementação de caminhos usa a operação perguntada; mencionar “fornecedor” na descrição de um parâmetro não acrescenta automaticamente um caminho de cadastro de fornecedor, nem um menu administrativo de parâmetros.

A seleção aceita uma correspondência real de assunto nos rótulos, sem exigir que verbos como “criar” apareçam literalmente em “Lançamento de Perdas”. A hierarquia do Menu resolve os ramos mais específicos; “ERP EvolutTech” não substitui o assunto da pergunta. Os formulários abertos pelos itens pertinentes do Menu são incluídos mesmo quando arquivos de nomes parecidos ocupam os 32 candidatos iniciais (até 16 destinos adicionais, sempre no mesmo projeto e com hash verificado). Os testes cobrem “eu consigo criar perdas no sistema”, verbos de lançamento e registro e uma pergunta contendo a marca, além de arquivos auxiliares e ramos alheios que compartilham palavras.

São 31 testes do painel e oito do analisador. Além das buscas e reconstruções, validam login do HELP somente por senha, senha BCrypt/legada, empresa e acesso ativo, revogação de sessão e a sequência HTTP com antifalsificação, cookie e logout, sem serviço de e-mail registrado (usuários de fixture, sem alterações no banco real). Os testes incluem mais arquivos auxiliares do que o limite anterior, um menu sem o assunto no nome do arquivo, parâmetros de uma rotina de nome genérico e a composição final do caminho e do checklist. A reconstrução também resolve o formulário de destino do menu sem exigir um cadastro de imagem por funcionalidade. Assim, verifica o mecanismo de busca sem cadastrar uma resposta fixa para perdas.

### Cadastro de produto, referências visuais e vídeos

A busca identifica a intenção de consultar o cadastro de produto e prioriza `CadProduto` no projeto dos cadastros (`KnowledgeSettings.ModuleProjects["Cadastros"]`; quando não informado, usa o projeto principal já selecionado para o PDV). Ao mudar de PDV para cadastro de produto, as perguntas anteriores do PDV não contaminam o assunto novo nem seus pedidos de esclarecimento. O módulo de um arquivo não é inferido a partir de nomes incidentais de métodos de outro produto.

Além da consulta de definições no SQL Server, a busca lê chamadas estáticas de `buscaParametroDinamico` nos arquivos verificados para recuperar nome, grupo e descrição. O ranking reserva evidências de interface, métodos, parâmetros, documentação, casos revisados e orientações, evitando que muitos parâmetros ou telas secundárias substituam a tela e os eventos principais. Campo de cadastro e parâmetro de liberação são tratados separadamente: o suporte ajusta a configuração, e o cliente utiliza o campo quando liberado. No caso conferido, `Código Balança` aparece em Gerais → Outros Dados do Produto → Dados de Balança e a digitação manual depende de `ALTERA_CODIGO_BALANCA_MANUAL`; o valor efetivo do cliente não foi consultado.

A reconstrução visual abrange quantidade no PDV, acesso ao fechamento por Funções PDV e o campo Código Balança do cadastro de produto. Os textos do `.resx`, o tipo de entrada inteira e controles de agrupamento/abas do cadastro entram no layout; a seta usa as coordenadas do controle após o layout nativo. O cache inclui a tela, os recursos e o vínculo do campo. Para o fechamento, a referência mostra F11 → 8 → Enter na tela Funções; não presume qual modalidade de caixa (normal/cego) será aberta após a permissão. Nas demais operações, o destino confirmado pelo evento do Menu permite reconstruir o formulário correspondente, como Lançamento de Perdas. Sem um campo específico confirmado, a referência mostra a tela inteira, sem seta. As fontes precisam estar indexadas e com hash válido; eventos do ERP e consultas de dados da tela não são executados. Caracteres de controle são removidos das respostas e os rótulos de menu recuperam a grafia verificada no Designer.

Quando uma resposta cita uma definição de parâmetro mas omite seu nome, a apresentação acrescenta o nome exato e orienta avaliação pelo suporte. Pedidos para o cliente alterar ou consultar a parametrização interna são substituídos por orientação ao suporte, preservando o caminho do campo na tela. Os atalhos também são registrados como ações estruturadas do Designer. Se a resposta mencionar uma ação, como Funções, e omitir o atalho declarado, a apresentação completa essa informação a partir da fonte e inclui a referência correspondente; não há tecla fixa presumida nessa regra.

Em Configurações → Vídeos do YouTube, o canal `https://www.youtube.com/@evoluttech7275` fica associado ao ID confirmado `UCJ95JKodmws25c07LItAN2g`. “Salvar e atualizar vídeos” importa os itens retornados pelo feed público; atualmente são os 15 vídeos recentes. Os links sugeridos são gerados apenas a partir dos IDs desses itens e escolhidos pelo assunto e módulo da dúvida. Por exemplo, a consulta sobre fechar o caixa encontra o vídeo do próprio canal sobre esse procedimento. O catálogo é local, não escreve no SQL Server e preserva os itens anteriores em caso de falha de atualização. Não há importação automática de transcrições nem análise do conteúdo completo dos vídeos: as sugestões são material complementar e não comprovam procedimentos ou funcionalidades. A resposta e seus vídeos ficam juntos em `SimulationAnswer`, disponíveis à futura API.

São 27 testes do painel e oito do analisador, incluindo troca de assunto, localização do campo e parâmetro, recursos da tela Funções e validação do canal/seleção de vídeos.

### Conferência da aplicação e cruzamento das fontes

Cada consulta busca evidências do código e da interface: textos visíveis, hierarquia dos menus, `ShortcutKeys`, rodapé e vínculos de eventos. Os Designers obedecem à seleção de projeto, módulo e hash do índice; arquivos de cópia são excluídos. Textos de `.resx` são lidos como XML, inclusive diretamente do ZIP do commit quando não foram extraídos: somente valores textuais, sem desserializar objetos ou executar o ERP. A consulta passa a reservar espaço para evidências de interface junto com os métodos da operação.

Antes de gerar a resposta, o serviço de inspeção registra se a interface foi localizada. Para o PDV, inclui a imagem PNG reconstruída do Designer na análise da OpenAI, já na primeira explicação textual. Assim, a escala texto → fluxo → imagem é a apresentação ao cliente; a conferência da interface acontece antes dela. A inspeção não é uma sessão ao vivo da instalação. Em outros módulos, o relatório informa quando há apenas declarações da interface, sem reconstrução visual. Perguntas de navegação precisam citar evidência de interface do módulo correto; somente um nome de método não comprova um menu ou atalho. A requisição usa `input_text` e, quando disponível, `input_image` segundo a [documentação oficial OpenAI de análise de imagens](https://developers.openai.com/api/docs/guides/images-vision). O modelo configurado precisa aceitar texto e imagem.

O cruzamento consulta orientações cadastradas, aplicação/interface, documentação autorizada ao público selecionado, definições de parâmetros e soluções semelhantes de atendimentos finalizados. Para os casos anteriores, usa `AiTicketInsights` ligado a `TicketChamadoC`, com `SolucaoConfirmada=1`, `StatusRevisao='aprovado'` e situação de finalizado. O mapeamento de `CentralTickets` usa `CodSituacao=3`, configurável em `Support:ClosedSituation`; não se confunde esse campo com `Status` ou com o catálogo geral `Situacao`. Restringe empresa, exige os termos relevantes da dúvida e prioriza o mesmo segmento. Somente pergunta/resumo de solução revisada, segmento e versão registrada entram como evidência; não são enviados diálogos de outros clientes. O nome do cliente anterior é removido antes do envio, além das máscaras de credenciais, contatos e documentos. Uma solução histórica sozinha não comprova a funcionalidade atual; deve ser corroborada pelas demais fontes. O modo comercial continua separado do histórico de suporte.

No simulador, “Cruzamento das fontes” mostra consultas, correspondências e indisponibilidades; “Conferência da aplicação” informa se houve imagem na análise. As notas técnicas devem registrar convergências, divergências e lacunas. A auditoria local guarda esses metadados, sem perguntas, mensagens ou imagens. As consultas ao SQL Server continuam somente de leitura. São 25 testes do painel e oito do analisador, incluindo menus/atalhos/rodapé, recursos XML, entrada visual na API, rejeição de navegação sem interface e necessidade de corroborar casos anteriores.

### Integrações posteriores

O processamento permanece em `SimulationService`, `IKnowledgeRetriever`, `ApplicationInspectionService` e `IAnswerProvider`, independente da interface Blazor. A API do Help e o chat do ERP poderão consumir essa mesma camada. A próxima etapa de integração deverá vincular empresa/cliente a uma identidade autenticada, controlar o histórico da conversa no servidor e devolver texto, formato, passos, referência visual e estado de resolução. As credenciais da OpenAI, GitHub e SQL permanecem no servidor; nenhuma chave deve ir para o cliente. Os endpoints e as conexões externas serão implementados nessa próxima etapa.

Para a dúvida de quantidade no PDV, a etapa de imagem também funciona sem captura cadastrada. O serviço reconstrói o `PDV.Designer.cs` do projeto selecionado nas configurações, verifica os hashes do Designer e do arquivo de código e calcula o destaque a partir da hierarquia de painéis e das coordenadas do campo de produto. O vínculo entre esse procedimento e esse campo foi verificado no código. Outras telas ainda precisam de vínculo verificado ou referência cadastrada; não se inferem coordenadas apenas do texto da IA.

A reconstrução usa um renderizador Windows Forms isolado, compilado do código próprio em `tools/ScreenRenderer.cs` pelo compilador do .NET Framework 4.x instalado no Windows. Na publicação, preserve essa pasta e instale o .NET Framework 4.8. O processo não carrega assemblies do ERP, não executa eventos nem consultas do ERP e não abre uma janela visível. O parser aceita somente tipos/propriedades visuais permitidos, literais do Designer e atribuições literais diretas do método de abertura. O resultado é armazenado em cache por hash em `App_Data/designer-screens`, com a identificação do commit. Controles especiais de entrada e grade são representados pelos controles nativos correspondentes; logos, imagens de recursos e alterações dependentes de dados, tema, resolução, parâmetros e instalação não são reproduzidos automaticamente. A referência não garante identidade visual com todas as instalações ou compatibilidade entre commit e versão.

As respostas de suporte terminam com “Conseguiu fazer? O problema foi resolvido?”. Uma negativa mantém o assunto e continua a sequência de orientação; uma confirmação explícita reconhece a resolução sem nova chamada à IA nem encerramento de chamado no banco. A conversa inclui essa confirmação no histórico para interpretar respostas curtas como “sim” e “não”.

No simulador, a primeira explicação é textual. Ao receber “não entendi”, a próxima resposta usa um fluxo; uma nova dificuldade leva a uma captura real da tela com seta e contorno sobre o campo indicado. O botão “Não entendi” permite testar a sequência. Em Configurações → Telas de referência, cadastre uma captura PNG de até 4 MB, módulo, palavras da dúvida, versão opcional, legenda e área de destaque; confira a prévia antes de salvar. As imagens ficam em `App_Data/screen-references.json`, fora da pasta pública; não são enviadas à OpenAI. O aplicativo monta um SVG que preserva a captura original e acrescenta as marcações cadastradas. Não inventa a aparência nem escolhe coordenadas sem revisão. A seleção exige correspondência de módulo e palavras-chave; versões preenchidas exigem correspondência exata. Referências sem versão são identificadas como gerais, sem garantir a correspondência com a instalação do cliente. Sem captura adequada, a interface informa que precisa dela e não substitui por uma imagem de fluxo.

Uma pergunta sobre outro assunto reinicia a apresentação em texto. Sem fontes suficientes ou sem dois passos válidos, o aplicativo mantém uma resposta textual e pede esclarecimento. Após a imagem, a IA deve perguntar em qual etapa surgiu a dúvida e orientar atendimento humano, sem afirmar que o acionou. As etapas passam pelas mesmas regras de citação, módulo e tratamento de parâmetros das respostas.

São 22 testes do painel e oito do analisador, incluindo progressão de apresentação, preservação do assunto, reinício por nova pergunta, bloqueio de imagens sem evidências, seleção de captura por módulo/assunto/versão, preservação do PNG original e escape do texto do SVG. Identificadores de controles e métodos ficam restritos à análise técnica; clientes recebem ações em linguagem comum. O procedimento de quantidade no PDV confirmado pelo responsável também está cadastrado em Orientações: quantidade → `*` → produto.

PDV, Consumer e Vendas têm evidências separadas. A seleção do projeto de origem do PDV fica nas configurações do GitHub, com persistência local em `knowledge-settings.json`; o projeto de produção confirmado é `Principal`. Essa seleção é aplicada às consultas e não exige nova indexação. A busca de procedimentos considera corpos de métodos e chamadas relacionadas, verificando o hash do arquivo indexado.

O campo `Grupo` de `ParametroDinamico` identifica o módulo quando reconhecido. Conflitos explícitos entre nome e grupo são excluídos das evidências da resposta. O cliente pode receber o nome do parâmetro para apresentar ao suporte; a avaliação e qualquer alteração ficam exclusivamente com o suporte. A definição do parâmetro não comprova seu valor no cliente. Valores e valores padrão não são enviados à IA.

Validação adicional: vinte testes do painel e oito do analisador aprovados, incluindo separação de módulos, seleção de projeto, procedimentos no código e encaminhamento de parâmetros ao suporte com preservação do nome. Uma geração real, sem identificação de cliente ou histórico, recuperou no PDV de `Principal` a sequência de digitar a quantidade e pressionar `*`, citando os métodos correspondentes. Essa verificação não garante todas as respostas futuras.

## Testes e validação

```powershell
dotnet run --project tests/EvolutTech.IA.Tests --no-build
dotnet run --project tests/AtendimentoIA.Tests --no-build
```

Oito testes do analisador e dezessete do painel cobrem herança/Forms, declarações parciais, dependências ausentes, fontes preservadas, persistência concorrente, arquivamento, criptografia, credenciais em redirecionamentos, extração segura, máscaras de texto, comparação numérica de versões, requisição Responses API sem identificadores, citações inválidas, ausência de chave/fontes, seleção por público, continuidade das perguntas e validação do hash do código, renderização sem bloqueio, recuperação de cache inválido e progresso real de download e análise. Fixtures ficam isoladas sob a pasta de saída dos testes. Os testes da API usam respostas de fixture em um transporte HTTP controlado; não consomem saldo nem substituem a geração real. Não foi possível validar uma geração real na OpenAI sem chave configurada.

Validação real dos catálogos SQL: 299 objetos, 3.556 colunas e 111 relacionamentos declarados. As seis telas e os arquivos CSS/Blazor responderam HTTP 200. A API pública GitHub retornou HTTP 404; isso não distingue um repositório privado de um endereço inexistente. A indexação real desse repositório depende de credencial válida e confirmação da referência de produção.

Referências: [Blazor Interactive Server](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-8.0), [arquivos do GitHub](https://docs.github.com/en/rest/repos/contents), [proteção de chaves no Windows](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest).
