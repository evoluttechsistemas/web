# EvolutDelivery

Loja de delivery multiempresa integrada ao ERP **AFV**. Cada empresa tem a sua loja em
`/EvolutDelivery/delivery/{slug}` e um painel administrativo em `/EvolutDelivery/admin-dashboard`.

## Tecnologia
- .NET 8 — Blazor Server (render mode `InteractiveServer`)
- SQL Server via ADO.NET (`Microsoft.Data.SqlClient`), sem ORM
- Autenticação por cookie (`EvolutDeliveryAuth`)
- Publicado no IIS com `UsePathBase("/EvolutDelivery")`

## Estrutura
```
Components/
  App.razor, Routes.razor        # AuthorizeRouteView: [Authorize] vale também na navegação interna
  RedirectToLogin.razor          # manda para o login quando não autenticado
  Layout/AdminLayout.razor       # menu do admin, "Ver Site" e "Sair"
  Pages/
    Home.razor                   # vitrine da loja (/delivery/{slug})
    Carrinho.razor               # carrinho
    Checkout.razor               # finalização em 4 etapas
    PedidoConfirmado.razor       # /pedido-confirmado/{token}
    MeusPedidos.razor            # acompanhamento por telefone (dados mascarados)
    Login.razor
    Admin*.razor                 # Dashboard, Produtos, Categorias, Clientes, Entregadores
Services/
  EmpresaService       # empresa por slug/código, geração de slug
  ProdutoService       # produtos, seções e imagem (ProdutoAFV)
  CarrinhoService      # monta o carrinho com preço/nome do BANCO
  StorageService       # localStorage do navegador
  ClienteService       # clientes (ClienteAFV) e endereços (ClienteEnderecoDelivery)
  PedidoService        # VendaCAFV + VendaDAFV + PedidoDelivery (em transação)
  PedidoLinkService    # token assinado do link "pedido confirmado"
  LoginService         # valida usuário + empresa do CNPJ
  EntregadorService
  CheckoutServices.cs  # FormaPagamentoService
Helpers/
  EmpresaLogadaHelper  # CodEmp/CNPJ do usuário logado (claims)
  PrivacidadeHelper    # mascara telefone e endereço
  SlugHelper, SqlDataReaderExtensions
```

## Regras importantes
- **Preço nunca vem do navegador.** O `localStorage` guarda só código, quantidade e observação
  (`carrinho_{slug}`). Carrinho e checkout buscam nome/preço no banco (`CarrinhoService`), e o
  checkout revalida tudo de novo antes de gravar o pedido.
- **Admin é por empresa.** Todas as páginas do admin usam `CodEmp` e `CNPJ` das claims do login
  (`EmpresaLogadaHelper`). Nada de CNPJ/código fixo no código.
- **Dados pessoais mascarados** nas telas públicas (endereço salvo "Rua X, nº ***", telefone "(**) *****-1234").
- **Link do pedido** usa token assinado (ASP.NET Data Protection), válido por 7 dias.
- **Imagens** são servidas por `GET /EvolutDelivery/img/{slug}/{codigo}` com cache HTTP
  (1 h + ETag). Não trafegam em base64 pelo SignalR.
- **Login**: `ReturnUrl` só aceita endereço local; a empresa escolhida precisa ser do CNPJ informado.
- **Código de cliente novo** (`ClienteAFV`) é gerado com `MAX+1` sob `UPDLOCK, HOLDLOCK` e nova
  tentativa em caso de chave duplicada (a tabela é do ERP, por isso não usa IDENTITY).

## Configuração
- Connection string `ConnectionComercial` no `appsettings.json`.
  Em produção, prefira variável de ambiente no IIS: `ConnectionStrings__ConnectionComercial`.
- `DetailedErrors` fica ligado só em desenvolvimento (`appsettings.Development.json`).
- Logs via `ILogger`. No IIS, para gravar em arquivo, ligue `stdoutLogEnabled` no `web.config`
  ou configure outro provedor de log.

## Rodar local
`http://localhost:5002/EvolutDelivery/delivery/{slug}`
