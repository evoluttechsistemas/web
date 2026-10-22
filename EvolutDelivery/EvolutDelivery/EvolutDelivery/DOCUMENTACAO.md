# 📦 EvolutDelivery - Documentação Completa do Sistema

## 📋 Índice
1. [Visão Geral](#visão-geral)
2. [Arquitetura do Sistema](#arquitetura-do-sistema)
3. [Tecnologias Utilizadas](#tecnologias-utilizadas)
4. [Modelos de Dados](#modelos-de-dados)
5. [Serviços e Funcionalidades](#serviços-e-funcionalidades)
6. [Fluxo de Operações](#fluxo-de-operações)
7. [Banco de Dados](#banco-de-dados)
8. [Sistema de Notificações](#sistema-de-notificações)
9. [Configuração e Deploy](#configuração-e-deploy)
10. [Guia de Uso](#guia-de-uso)

---

## 🎯 Visão Geral

O **EvolutDelivery** é um sistema completo de gerenciamento de delivery desenvolvido em .NET 8 com Blazor Server. O sistema permite o gerenciamento end-to-end de pedidos de delivery, desde o cadastro de produtos até a entrega ao cliente final, incluindo rastreamento em tempo real e gestão de entregadores.

### Principais Características

✅ **Gestão Completa de Pedidos**
- Criação e acompanhamento de pedidos
- Atualização de status em tempo real
- Histórico completo de pedidos

✅ **Gestão de Clientes**
- Cadastro de clientes com múltiplos endereços
- Histórico de compras
- Perfil de consumo

✅ **Gestão de Entregadores**
- Cadastro de entregadores e veículos
- Controle de disponibilidade
- Rastreamento GPS em tempo real
- Estatísticas de entregas

✅ **Catálogo de Produtos**
- Busca por produto ou categoria
- Imagens dos produtos
- Preços e promoções
- Organização por seções

✅ **Carrinho de Compras**
- Adição/remoção de produtos
- Cálculo automático de totais
- Observações personalizadas

✅ **Sistema de Notificações em Tempo Real**
- Notificações instantâneas via SignalR
- Atualização automática de status
- Alertas para novos pedidos

---

## 🏗️ Arquitetura do Sistema

### Estrutura do Projeto

```
EvolutDelivery/
│
├── Models/                    # Modelos de dados
│   ├── Produto.cs            # Modelo de produto
│   ├── Cliente.cs            # Modelo de cliente e endereços
│   ├── Pedido.cs             # Modelo de pedido e itens
│   ├── Entregador.cs         # Modelo de entregador
│   ├── ItemCarrinho.cs       # Item do carrinho de compras
│   ├── Categoria.cs          # Categorias, promoções e avaliações
│   └── SecaoModel.cs         # Seções de produtos
│
├── Services/                  # Camada de serviços
│   ├── ProdutoService.cs     # Operações com produtos
│   ├── ClienteService.cs     # Operações com clientes
│   ├── PedidoService.cs      # Operações com pedidos
│   ├── EntregadorService.cs  # Operações com entregadores
│   ├── NotificacaoService.cs # Sistema de notificações
│   ├── VendaService.cs       # Operações de venda
│   └── StorageService.cs     # Gerenciamento de storage
│
├── Helpers/                   # Classes auxiliares
│   └── SqlDataReaderExtensions.cs  # Extensões para leitura de dados
│
├── Components/                # Componentes Blazor (inferido)
│
└── Program.cs                 # Configuração da aplicação

```

### Padrões Arquiteturais

- **Service Layer Pattern**: Separação clara entre lógica de negócio e acesso a dados
- **Repository Pattern**: Abstração do acesso ao banco de dados
- **Dependency Injection**: Injeção de dependências nativa do .NET
- **Real-Time Communication**: SignalR para comunicação em tempo real

---

## 💻 Tecnologias Utilizadas

### Framework e Linguagem
- **.NET 8.0**: Framework principal
- **C# 12**: Linguagem de programação
- **Blazor Server**: Framework para UI interativa

### Bibliotecas e Pacotes
- **Dapper 2.1.66**: Micro ORM para acesso a dados de alta performance
- **Entity Framework Core 9.0.9**: ORM completo (SQL Server)
- **SignalR**: Comunicação em tempo real
- **Microsoft.Data.SqlClient**: Conexão com SQL Server

### Banco de Dados
- **SQL Server**: Banco de dados relacional

### Funcionalidades do .NET Utilizadas
- **Nullable Reference Types**: Habilitado para maior segurança
- **Implicit Usings**: Simplificação de imports
- **Async/Await**: Operações assíncronas para melhor performance

---

## 📊 Modelos de Dados

### 1. Produto

Representa os produtos disponíveis para venda no delivery.

```csharp
public class Produto
{
    public int Codigo { get; set; }
    public string Nome { get; set; }
    public decimal PrecoVenda { get; set; }
    public decimal PrecoPromocao { get; set; }
    public byte[] Imagem { get; set; }
    public string ImagemBase64 { get; }  // Conversão automática para exibição
}
```

**Características:**
- Armazena imagens em formato binário
- Conversão automática para Base64 para exibição em HTML
- Suporte a preços promocionais

---

### 2. Cliente e Endereço

Gerencia os dados dos clientes e seus endereços de entrega.

```csharp
public class Cliente
{
    public int Codigo { get; set; }
    public string Nome { get; set; }
    public string Apelido { get; set; }
    public string Telefone { get; set; }
    public string Email { get; set; }
    public string CPF { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime? DataNascimento { get; set; }
    public string? SenhaHash { get; set; }
    public bool Ativo { get; set; }
    public List<Endereco> Enderecos { get; set; }
    public int TotalPedidos { get; set; }
    public decimal TotalGasto { get; set; }
    public int CodEmp { get; set; }
}

public class Endereco
{
    public int Codigo { get; set; }
    public int CodCliente { get; set; }
    public string Descricao { get; set; }  // Casa, Trabalho, etc
    public string Rua { get; set; }
    public string Numero { get; set; }
    public string Complemento { get; set; }
    public string Bairro { get; set; }
    public string Cidade { get; set; }
    public string Estado { get; set; }
    public string CEP { get; set; }
    public string? PontoReferencia { get; set; }
    public bool EnderecoPrincipal { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
}
```

**Características:**
- Suporte a múltiplos endereços por cliente
- Definição de endereço principal
- Coordenadas GPS para rastreamento
- Autenticação com senha hash
- Estatísticas de compra (total de pedidos e gastos)

---

### 3. Pedido e ItemPedido

Representa os pedidos realizados pelos clientes.

```csharp
public class Pedido
{
    // Identificação
    public int Codigo { get; set; }
    public int CodCliente { get; set; }
    
    // Dados do Cliente
    public string NomeCliente { get; set; }
    public string TelefoneCliente { get; set; }
    public string EmailCliente { get; set; }
    
    // Endereço de Entrega
    public int? CodEndereco { get; set; }
    public string EnderecoCompleto { get; set; }
    public string Bairro { get; set; }
    public string Cidade { get; set; }
    public string CEP { get; set; }
    public string Complemento { get; set; }
    public string Numero { get; set; }
    
    // Valores
    public DateTime DataPedido { get; set; }
    public DateTime? DataEntrega { get; set; }
    public decimal ValorSubtotal { get; set; }
    public decimal ValorEntrega { get; set; }
    public decimal ValorDesconto { get; set; }
    public decimal ValorTotal { get; set; }
    
    // Status e Controle
    public string Status { get; set; }  // PENDENTE, CONFIRMADO, PREPARANDO, SAIU_ENTREGA, ENTREGUE, CANCELADO
    public string FormaPagamento { get; set; }  // DINHEIRO, PIX, CARTAO, ONLINE
    public string TipoEntrega { get; set; }  // ENTREGA, RETIRADA
    public string Observacao { get; set; }
    
    // Entregador
    public int? CodEntregador { get; set; }
    public string NomeEntregador { get; set; }
    
    // Rastreamento
    public string? LatitudeEntrega { get; set; }
    public string? LongitudeEntrega { get; set; }
    public DateTime? UltimaAtualizacaoLocalizacao { get; set; }
    
    // Controle Interno
    public string Usuario { get; set; }
    public int CodEmp { get; set; }
    public bool Notificado { get; set; }
    
    // Relacionamentos
    public List<ItemPedido> Itens { get; set; }
    
    // Helpers para UI
    public string StatusFormatado { get; }
    public string StatusClass { get; }
    public bool PodeEditar { get; }
    public bool PodeCancelar { get; }
    public bool EmAndamento { get; }
}

public class ItemPedido
{
    public int Codigo { get; set; }
    public int CodPedido { get; set; }
    public int CodProduto { get; set; }
    public string NomeProduto { get; set; }
    public decimal PrecoUnitario { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoTotal { get; set; }
    public string Observacao { get; set; }
    public byte[]? ImagemProduto { get; set; }
}
```

**Características:**
- Ciclo completo de vida do pedido (6 status diferentes)
- Suporte a múltiplas formas de pagamento
- Rastreamento GPS da entrega
- Cálculo automático de valores
- Validações de negócio (pode editar, pode cancelar)
- Formatação automática para UI

---

### 4. Entregador

Gerencia os dados dos entregadores e seus veículos.

```csharp
public class Entregador
{
    // Identificação
    public int Codigo { get; set; }
    public string Nome { get; set; }
    public string Telefone { get; set; }
    public string Email { get; set; }
    public string CPF { get; set; }
    
    // Veículo
    public string TipoVeiculo { get; set; }  // MOTO, CARRO, BICICLETA, A_PE
    public string? PlacaVeiculo { get; set; }
    public string? CorVeiculo { get; set; }
    
    // Status
    public bool Ativo { get; set; }
    public bool Disponivel { get; set; }
    public string StatusAtual { get; set; }  // LIVRE, EM_ENTREGA, PAUSADO
    
    // Localização Atual
    public string? LatitudeAtual { get; set; }
    public string? LongitudeAtual { get; set; }
    public DateTime? UltimaAtualizacaoGPS { get; set; }
    
    // Estatísticas
    public int TotalEntregas { get; set; }
    public decimal AvaliacaoMedia { get; set; }
    public int TotalAvaliacoes { get; set; }
    
    public int CodEmp { get; set; }
    public DateTime DataCadastro { get; set; }
    
    // Helpers
    public string VeiculoFormatado { get; }
    public string StatusFormatado { get; }
    public bool PodeReceber { get; }
}
```

**Características:**
- Suporte a diferentes tipos de veículos
- Rastreamento GPS em tempo real
- Sistema de avaliações
- Controle de disponibilidade inteligente
- Estatísticas de performance

---

### 5. Categoria, Promoção e Avaliação

Modelos auxiliares para funcionalidades extras.

```csharp
public class Categoria
{
    public int Codigo { get; set; }
    public string Nome { get; set; }
    public string? Icone { get; set; }
    public string? Cor { get; set; }
    public int Ordem { get; set; }
    public bool Ativo { get; set; }
    public int CodEmp { get; set; }
    public byte[]? Imagem { get; set; }
}

public class Promocao
{
    public int Codigo { get; set; }
    public string Nome { get; set; }
    public string Descricao { get; set; }
    public string TipoDesconto { get; set; }  // PERCENTUAL, VALOR_FIXO
    public decimal ValorDesconto { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime DataFim { get; set; }
    public bool Ativo { get; set; }
    public int? CodProduto { get; set; }
    public int CodEmp { get; set; }
    public bool EstaAtiva { get; }
    public string DescontoFormatado { get; }
}

public class Avaliacao
{
    public int Codigo { get; set; }
    public int CodPedido { get; set; }
    public int CodCliente { get; set; }
    public string NomeCliente { get; set; }
    public int Estrelas { get; set; }  // 1 a 5
    public string? Comentario { get; set; }
    public DateTime DataAvaliacao { get; set; }
    public int? CodProduto { get; set; }
    public int? CodEntregador { get; set; }
    public string EstrelasSimbolo { get; }
}
```

---

### 6. ItemCarrinho

Representa itens temporários no carrinho de compras.

```csharp
public class ItemCarrinho
{
    public int Codigo { get; set; }
    public string Nome { get; set; }
    public int Quantidade { get; set; }
    public decimal Preco { get; set; }
    public string ImagemBase64 { get; set; }
    public string Observacao { get; set; }
}
```

---

## 🔧 Serviços e Funcionalidades

### 1. ProdutoService

**Responsabilidade:** Gerenciar o catálogo de produtos.

**Métodos Principais:**

```csharp
// Buscar produtos por termo de pesquisa
List<Produto> BuscarProdutos(string busca, string cnpj)

// Buscar produtos por seção/categoria
List<Produto> BuscarProdutosPorSecao(int codSecao, string cnpj)

// Buscar seções/categorias disponíveis
List<SecaoModel> BuscarSecoes(string cnpj)
```

**Funcionalidades:**
- Busca de produtos por nome (LIKE)
- Filtro por seção/categoria
- Limitação de 100 produtos por consulta
- Carregamento de imagens do banco
- Suporte multi-empresa (CNPJ)

**Tabelas Utilizadas:** `ProdutoAFV`

---

### 2. ClienteService

**Responsabilidade:** Gerenciar clientes e seus endereços.

**Métodos Principais:**

```csharp
// Buscar cliente por telefone
Task<Cliente?> BuscarPorTelefoneAsync(string telefone, int codEmp)

// Criar ou atualizar cliente
Task<int> SalvarClienteAsync(Cliente cliente)

// Buscar endereços do cliente
Task<List<Endereco>> BuscarEnderecosAsync(int codCliente)

// Salvar endereço
Task<int> SalvarEnderecoAsync(Endereco endereco)
```

**Funcionalidades:**
- Cadastro completo de clientes
- Gestão de múltiplos endereços
- Busca rápida por telefone
- Atualização de dados cadastrais
- Suporte a endereço principal

**Tabelas Utilizadas:** `ClienteDelivery`, `EnderecoDelivery`

---

### 3. PedidoService

**Responsabilidade:** Gerenciar todo o ciclo de vida dos pedidos.

**Métodos Principais:**

```csharp
// Criar novo pedido
Task<int> CriarPedidoAsync(Pedido pedido, List<ItemCarrinho> itens)

// Listar pedidos com filtros
Task<List<Pedido>> ListarPedidosAsync(int codEmp, string? status, DateTime? dataInicio, DateTime? dataFim)

// Buscar pedido específico
Task<Pedido?> BuscarPorIdAsync(int codigo)

// Atualizar status do pedido
Task<bool> AtualizarStatusAsync(int codigo, string novoStatus, int? codEntregador)

// Buscar itens do pedido
Task<List<ItemPedido>> BuscarItensPedidoAsync(int codPedido)
```

**Funcionalidades:**
- Criação transacional de pedidos (pedido + itens)
- Filtros avançados (status, data, empresa)
- Atualização de status com notificações
- Relacionamento com entregadores
- Cálculo automático de valores
- Rollback automático em caso de erro

**Fluxo de Status:**
1. **PENDENTE** → Pedido criado, aguardando confirmação
2. **CONFIRMADO** → Pedido confirmado pelo estabelecimento
3. **PREPARANDO** → Pedido em preparo
4. **SAIU_ENTREGA** → Pedido saiu para entrega
5. **ENTREGUE** → Pedido entregue ao cliente
6. **CANCELADO** → Pedido cancelado

**Tabelas Utilizadas:** `PedidoDelivery`, `ItemPedidoDelivery`

---

### 4. EntregadorService

**Responsabilidade:** Gerenciar entregadores e suas entregas.

**Métodos Principais:**

```csharp
// Listar todos os entregadores
Task<List<Entregador>> ListarTodosAsync(int codEmp, bool apenasAtivos)

// Buscar entregadores disponíveis
Task<List<Entregador>> BuscarDisponiveisAsync(int codEmp)

// Atualizar status do entregador
Task<bool> AtualizarStatusAsync(int codigo, string novoStatus)

// Atualizar localização GPS
Task<bool> AtualizarLocalizacaoAsync(int codigo, string latitude, string longitude)

// Registrar entrega concluída
Task<bool> RegistrarEntregaConcluidaAsync(int codigo)

// Salvar/Atualizar entregador
Task<int> SalvarAsync(Entregador entregador)
```

**Funcionalidades:**
- Cadastro completo de entregadores
- Controle de disponibilidade em tempo real
- Rastreamento GPS
- Estatísticas de entregas
- Atualização automática de status
- Ordenação por experiência (total de entregas)

**Lógica de Disponibilidade:**
- Status LIVRE + Disponível = Pode receber novos pedidos
- Status EM_ENTREGA = Ocupado com entrega
- Status PAUSADO = Temporariamente indisponível

**Tabelas Utilizadas:** `Entregador`

---

### 5. NotificacaoService

**Responsabilidade:** Gerenciar notificações em tempo real via SignalR.

**Hub SignalR:**
```csharp
public class NotificacaoHub : Hub
{
    // Enviar notificação de pedido
    Task EnviarNotificacaoPedido(int codPedido, string mensagem)
    
    // Atualizar status de pedido
    Task AtualizarStatusPedido(int codPedido, string novoStatus)
    
    // Notificar novo pedido para administradores
    Task NotificarNovoPedido(Pedido pedido)
}
```

**Serviço:**
```csharp
public class NotificacaoService
{
    // Notificar novo pedido
    Task NotificarNovoPedidoAsync(Pedido pedido)
    
    // Notificar mudança de status
    Task NotificarMudancaStatusAsync(int codPedido, string novoStatus, string telefoneCliente)
    
    // Notificar entregador específico
    Task NotificarEntregadorAsync(int codEntregador, string mensagem)
}
```

**Funcionalidades:**
- Notificações em tempo real
- Broadcasting para todos os clientes conectados
- Grupos de notificação (administradores, entregadores)
- Atualização automática de UI
- Alertas sonoros/visuais

**Endpoint SignalR:** `/notificacoes`

---

### 6. VendaService e StorageService

**VendaService:** Gerencia operações de venda (inferido, código não fornecido)
**StorageService:** Gerencia armazenamento local/sessão (inferido, código não fornecido)

---

## 🔄 Fluxo de Operações

### Fluxo Completo de um Pedido

```
┌─────────────────────────────────────────────────────────────────┐
│                     CLIENTE                                      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    1. Navega no Catálogo
                       (ProdutoService)
                              │
                              ▼
                    2. Adiciona ao Carrinho
                       (ItemCarrinho)
                              │
                              ▼
                    3. Finaliza Pedido
                       (PedidoService.CriarPedidoAsync)
                              │
┌─────────────────────────────┴─────────────────────────────┐
│                                                             │
▼                                                             ▼
4a. Pedido Salvo no Banco                    4b. Notificação em Tempo Real
    (PedidoDelivery + ItemPedidoDelivery)         (NotificacaoService)
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                   ESTABELECIMENTO                                │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    5. Confirma Pedido
                       (Status: CONFIRMADO)
                              │
                              ▼
                    6. Inicia Preparo
                       (Status: PREPARANDO)
                              │
                              ▼
                    7. Atribui Entregador
                       (EntregadorService.BuscarDisponiveisAsync)
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     ENTREGADOR                                   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    8. Coleta o Pedido
                       (Status: SAIU_ENTREGA)
                              │
                              ▼
                    9. Rastreamento GPS
                       (EntregadorService.AtualizarLocalizacaoAsync)
                              │
                              ▼
                    10. Entrega ao Cliente
                        (Status: ENTREGUE)
                              │
                              ▼
                    11. Atualiza Estatísticas
                        (EntregadorService.RegistrarEntregaConcluidaAsync)
```

### Fluxo de Cadastro de Cliente

```
Cliente Existente?
│
├─ NÃO ──► ClienteService.BuscarPorTelefoneAsync (null)
│          │
│          └──► ClienteService.SalvarClienteAsync (INSERT)
│                │
│                └──► Retorna CodCliente
│
└─ SIM ──► ClienteService.BuscarPorTelefoneAsync (Cliente)
           │
           └──► Usa Cliente Existente
                │
                └──► Busca Endereços (ClienteService.BuscarEnderecosAsync)
```

### Fluxo de Atualização de Status

```
Pedido.Status = "CONFIRMADO"
│
└──► PedidoService.AtualizarStatusAsync(codigo, "PREPARANDO")
      │
      ├──► 1. Atualiza no Banco de Dados
      │
      ├──► 2. NotificacaoService.NotificarMudancaStatusAsync
      │     │
      │     └──► SignalR Broadcasting
      │           │
      │           ├──► Cliente recebe notificação
      │           ├──► Dashboard atualiza
      │           └──► Entregador é alertado
      │
      └──► 3. Retorna Sucesso
```

---

## 💾 Banco de Dados

### Estrutura de Tabelas

#### 1. ProdutoAFV
```sql
Campos Principais:
- Codigo (INT, PK)
- Descricao (VARCHAR)
- PrecoVenda (DECIMAL)
- PrecoPromocao (DECIMAL)
- Imagem (VARBINARY)
- CodSecao (INT)
- NomeSecao (VARCHAR)
- CNPJ (VARCHAR)
```

#### 2. ClienteDelivery
```sql
Campos Principais:
- Codigo (INT, PK, IDENTITY)
- Nome (VARCHAR)
- Apelido (VARCHAR)
- Telefone (VARCHAR, UNIQUE)
- Email (VARCHAR)
- CPF (VARCHAR)
- DataCadastro (DATETIME)
- DataNascimento (DATETIME)
- SenhaHash (VARCHAR)
- Ativo (BIT)
- CodEmp (INT)
```

#### 3. EnderecoDelivery
```sql
Campos Principais:
- Codigo (INT, PK, IDENTITY)
- CodCliente (INT, FK)
- Descricao (VARCHAR)
- Rua (VARCHAR)
- Numero (VARCHAR)
- Complemento (VARCHAR)
- Bairro (VARCHAR)
- Cidade (VARCHAR)
- Estado (VARCHAR)
- CEP (VARCHAR)
- PontoReferencia (VARCHAR)
- EnderecoPrincipal (BIT)
- Latitude (VARCHAR)
- Longitude (VARCHAR)
```

#### 4. PedidoDelivery
```sql
Campos Principais:
- Codigo (INT, PK, IDENTITY)
- CodCliente (INT, FK)
- NomeCliente (VARCHAR)
- TelefoneCliente (VARCHAR)
- EmailCliente (VARCHAR)
- EnderecoCompleto (VARCHAR)
- Bairro (VARCHAR)
- Cidade (VARCHAR)
- CEP (VARCHAR)
- Complemento (VARCHAR)
- Numero (VARCHAR)
- DataPedido (DATETIME)
- DataEntrega (DATETIME)
- ValorSubtotal (DECIMAL)
- ValorEntrega (DECIMAL)
- ValorDesconto (DECIMAL)
- ValorTotal (DECIMAL)
- Status (VARCHAR)
- FormaPagamento (VARCHAR)
- TipoEntrega (VARCHAR)
- Observacao (TEXT)
- CodEntregador (INT, FK)
- LatitudeEntrega (VARCHAR)
- LongitudeEntrega (VARCHAR)
- UltimaAtualizacaoLocalizacao (DATETIME)
- Usuario (VARCHAR)
- CodEmp (INT)
- Notificado (BIT)
```

#### 5. ItemPedidoDelivery
```sql
Campos Principais:
- Codigo (INT, PK, IDENTITY)
- CodPedido (INT, FK)
- CodProduto (INT, FK)
- NomeProduto (VARCHAR)
- PrecoUnitario (DECIMAL)
- Quantidade (INT)
- PrecoTotal (DECIMAL)
- Observacao (TEXT)
```

#### 6. Entregador
```sql
Campos Principais:
- Codigo (INT, PK, IDENTITY)
- Nome (VARCHAR)
- Telefone (VARCHAR)
- Email (VARCHAR)
- CPF (VARCHAR)
- TipoVeiculo (VARCHAR)
- PlacaVeiculo (VARCHAR)
- CorVeiculo (VARCHAR)
- Ativo (BIT)
- Disponivel (BIT)
- StatusAtual (VARCHAR)
- LatitudeAtual (VARCHAR)
- LongitudeAtual (VARCHAR)
- UltimaAtualizacaoGPS (DATETIME)
- TotalEntregas (INT)
- AvaliacaoMedia (DECIMAL)
- TotalAvaliacoes (INT)
- CodEmp (INT)
- DataCadastro (DATETIME)
```

### String de Conexão

A connection string é configurada no `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ConnectionComercial": "Server=SERVIDOR;Database=BANCO;User Id=USUARIO;Password=SENHA;"
  }
}
```

---

## 📡 Sistema de Notificações

### SignalR Hub

O sistema utiliza **SignalR** para comunicação em tempo real entre servidor e clientes.

**Endpoint:** `/notificacoes`

**Eventos Disponíveis:**

1. **ReceberNotificacao**
   - Notificação genérica de pedido
   - Parâmetros: `(int codPedido, string mensagem)`

2. **StatusPedidoAtualizado**
   - Atualização de status
   - Parâmetros: `(int codPedido, string novoStatus)`

3. **NovoPedido**
   - Novo pedido criado
   - Parâmetros: `(Pedido pedido)`

4. **StatusAtualizado**
   - Status atualizado com detalhes
   - Parâmetros: `{ CodPedido, Status, Telefone }`

5. **NotificacaoEntregador**
   - Notificação específica para entregador
   - Parâmetros: `(string mensagem)`

### Configuração do SignalR

```csharp
// No Program.cs
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(6);
        options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(10);
        options.MaxBufferedUnacknowledgedRenderBatches = 20;
    });

builder.Services.AddSignalR(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
    options.KeepAliveInterval = TimeSpan.FromMinutes(3);
    options.HandshakeTimeout = TimeSpan.FromSeconds(60);
});
```

**Características:**
- Timeout de 10 minutos para clientes
- Keep-alive a cada 3 minutos
- Retenção de circuito desconectado por 6 horas
- Buffer de 20 renderizações não confirmadas

---

## ⚙️ Configuração e Deploy

### Requisitos do Sistema

- **.NET 8 SDK** ou superior
- **SQL Server** 2016 ou superior
- **Windows Server** ou **Linux** com suporte a .NET
- Mínimo **2GB RAM** recomendado
- Conexão estável com internet

### Passos de Instalação

1. **Clone o repositório**
   ```bash
   git clone [URL_DO_REPOSITORIO]
   cd EvolutDelivery
   ```

2. **Configure a string de conexão**
   
   Edite `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "ConnectionComercial": "Server=SEU_SERVIDOR;Database=SEU_BANCO;User Id=SEU_USUARIO;Password=SUA_SENHA;"
     }
   }
   ```

3. **Restaure os pacotes**
   ```bash
   dotnet restore
   ```

4. **Execute a aplicação**
   ```bash
   dotnet run
   ```

5. **Acesse no navegador**
   ```
   https://localhost:5001
   ```

### Publicação

```bash
dotnet publish -c Release -o ./publish
```

### Configuração do IIS

1. Instale o **.NET 8 Hosting Bundle**
2. Crie um novo **Application Pool** (.NET CLR Version: No Managed Code)
3. Configure o site apontando para a pasta `publish`
4. Ajuste permissões de acesso ao banco de dados

---

## 📱 Guia de Uso

### Para Clientes

1. **Navegar pelo Catálogo**
   - Acesse a página inicial
   - Navegue pelas seções ou use a busca
   - Visualize produtos com imagens e preços

2. **Fazer um Pedido**
   - Adicione produtos ao carrinho
   - Informe seus dados de contato
   - Selecione ou cadastre um endereço
   - Escolha a forma de pagamento
   - Confirme o pedido

3. **Acompanhar Pedido**
   - Receba notificações em tempo real
   - Acompanhe o status do pedido
   - Veja informações do entregador

### Para Administradores

1. **Gerenciar Pedidos**
   - Visualize todos os pedidos
   - Filtre por status, data, cliente
   - Confirme ou cancele pedidos
   - Atribua entregadores

2. **Gerenciar Produtos**
   - Cadastre novos produtos
   - Atualize preços e promoções
   - Organize por seções/categorias

3. **Gerenciar Entregadores**
   - Cadastre entregadores
   - Verifique disponibilidade
   - Acompanhe entregas em andamento
   - Visualize estatísticas

4. **Dashboard**
   - Acompanhe pedidos em tempo real
   - Receba alertas de novos pedidos
   - Monitore performance

### Para Entregadores

1. **Receber Pedidos**
   - Marque-se como disponível
   - Receba notificações de novos pedidos
   - Aceite ou recuse entregas

2. **Realizar Entrega**
   - Visualize detalhes do pedido
   - Veja endereço e mapa
   - Atualize status conforme progresso
   - Confirme entrega

3. **Atualizar Localização**
   - GPS atualizado automaticamente
   - Cliente acompanha em tempo real

---

## 🔐 Segurança

### Medidas Implementadas

1. **Autenticação**
   - Senhas armazenadas com hash
   - Validação de credenciais

2. **Autorização**
   - Controle por empresa (CodEmp)
   - Separação de dados por organização

3. **Validação de Dados**
   - Nullable reference types
   - Validação de entrada
   - Proteção contra SQL Injection (uso de parâmetros)

4. **Comunicação**
   - HTTPS obrigatório em produção
   - SignalR com autenticação
   - HSTS habilitado

---

## 📊 Indicadores e Estatísticas

O sistema rastreia diversos indicadores:

### Por Cliente
- Total de pedidos realizados
- Valor total gasto
- Frequência de compra
- Endereços cadastrados

### Por Entregador
- Total de entregas realizadas
- Avaliação média
- Total de avaliações
- Taxa de sucesso

### Por Pedido
- Tempo médio de preparo
- Tempo médio de entrega
- Taxa de cancelamento
- Ticket médio

---

## 🚀 Funcionalidades Futuras (Sugeridas)

1. **Pagamento Online**
   - Integração com gateways de pagamento
   - PIX automático
   - Cartão de crédito

2. **Sistema de Avaliações**
   - Avaliação de produtos
   - Avaliação de entregadores
   - Comentários e fotos

3. **Programa de Fidelidade**
   - Pontos por compra
   - Cupons de desconto
   - Cashback

4. **Aplicativo Mobile**
   - App para clientes (Android/iOS)
   - App para entregadores

5. **Relatórios Avançados**
   - Dashboard gerencial
   - Relatórios de vendas
   - Análise de performance

6. **Integrações**
   - WhatsApp Business API
   - SMS para notificações
   - Google Maps para rotas otimizadas

---

## 📞 Suporte e Manutenção

### Logs e Troubleshooting

- Logs da aplicação: `logs/` (configurar no Program.cs)
- Erros do SignalR: Console do navegador
- Erros de banco: SQL Server Profiler

### Contatos

- **Desenvolvimento:** [Email do Desenvolvedor]
- **Suporte Técnico:** [Email do Suporte]
- **Documentação:** Este arquivo

---

## 📄 Licença

[Definir tipo de licença]

---

## 👥 Contribuidores

- **Desenvolvedor Principal:** [Nome]
- **Equipe:** [Nomes]

---

## 📚 Referências

- [Documentação .NET 8](https://docs.microsoft.com/dotnet/)
- [Blazor Documentation](https://docs.microsoft.com/aspnet/core/blazor/)
- [SignalR Documentation](https://docs.microsoft.com/aspnet/core/signalr/)
- [Dapper GitHub](https://github.com/DapperLib/Dapper)
- [Entity Framework Core](https://docs.microsoft.com/ef/core/)

---

**Versão da Documentação:** 1.0  
**Data:** 2024  
**Última Atualização:** [Data Atual]

---

## 🎓 Glossário

- **CNPJ**: Cadastro Nacional de Pessoa Jurídica
- **GPS**: Global Positioning System (Sistema de Posicionamento Global)
- **Hub**: Ponto central de conexão no SignalR
- **ORM**: Object-Relational Mapping (Mapeamento Objeto-Relacional)
- **SDK**: Software Development Kit
- **SignalR**: Biblioteca para comunicação em tempo real
- **TFM**: Target Framework Moniker

---

*Esta documentação foi gerada automaticamente com base na análise do código-fonte do projeto EvolutDelivery.*
