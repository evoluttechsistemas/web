# 🍔 EvolutDelivery - Sistema Completo de Delivery

## 📋 Índice
- [Sobre o Projeto](#sobre-o-projeto)
- [Funcionalidades](#funcionalidades)
- [Tecnologias](#tecnologias)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Como Usar](#como-usar)
- [Configuração](#configuração)
- [Módulos](#módulos)

---

## 🎯 Sobre o Projeto

**EvolutDelivery** é um sistema completo de delivery desenvolvido em **Blazor Server (.NET 8)** que oferece uma solução moderna e profissional para restaurantes, lanchonetes e estabelecimentos que desejam gerenciar pedidos online com eficiência.

### ✨ Diferenciais
- ✅ **Interface Moderna**: UI responsiva e intuitiva
- ✅ **Rastreamento em Tempo Real**: Acompanhe pedidos e entregadores
- ✅ **Gestão Completa**: Admin, clientes e entregadores
- ✅ **Integração WhatsApp**: Envio de pedidos direto no WhatsApp
- ✅ **Multi-Empresa**: Suporte para múltiplas empresas
- ✅ **Sistema de Avaliações**: Feedback de clientes
- ✅ **Promoções**: Gerenciamento de descontos e ofertas

---

## 🚀 Funcionalidades

### 🛍️ **Para Clientes**
- Catálogo de produtos com busca e filtros
- Carrinho de compras dinâmico
- Checkout simplificado
- Acompanhamento de pedidos em tempo real
- Histórico de pedidos
- Múltiplos endereços de entrega
- Avaliações de produtos e entregas

### 🎯 **Para Administradores**
- Painel de controle completo
- Gestão de pedidos (confirmar, preparar, despachar)
- Gerenciamento de entregadores
- Relatórios e estatísticas
- Gestão de produtos e categorias
- Configuração de promoções
- Gerenciamento de clientes

### 🚗 **Para Entregadores**
- Visualização de pedidos atribuídos
- Atualização de status em tempo real
- Rastreamento GPS (futuro)
- Histórico de entregas
- Sistema de avaliações

---

## 🛠️ Tecnologias

| Tecnologia | Versão | Uso |
|------------|--------|-----|
| **.NET** | 8.0 | Framework principal |
| **Blazor Server** | 8.0 | Interface web |
| **C#** | 12.0 | Linguagem de programação |
| **SQL Server** | 2019+ | Banco de dados |
| **SignalR** | 8.0 | Comunicação em tempo real |

---

## 📁 Estrutura do Projeto

```
EvolutDelivery/
│
├── Models/                    # Modelos de dados
│   ├── Pedido.cs             # Modelo de pedido
│   ├── Cliente.cs            # Modelo de cliente
│   ├── Entregador.cs         # Modelo de entregador
│   ├── Produto.cs            # Modelo de produto
│   ├── Categoria.cs          # Categorias e promoções
│   └── ItemCarrinho.cs       # Item do carrinho
│
├── Services/                  # Serviços de negócio
│   ├── PedidoService.cs      # Gestão de pedidos
│   ├── ClienteService.cs     # Gestão de clientes
│   ├── EntregadorService.cs  # Gestão de entregadores
│   ├── ProdutoService.cs     # Gestão de produtos
│   ├── VendaService.cs       # Integração de vendas
│   └── StorageService.cs     # Armazenamento local
│
├── Components/
│   └── Pages/                 # Páginas Blazor
│       ├── Home.razor         # Cardápio
│       ├── Carrinho.razor     # Carrinho de compras
│       ├── Checkout.razor     # Finalização de pedido
│       ├── AdminPedidos.razor # Painel administrativo
│       ├── MeusPedidos.razor  # Pedidos do cliente
│       └── PedidoConfirmado.razor # Confirmação
│
└── Database/
    └── CriarTabelas.sql       # Script de criação do banco
```

---

## ⚙️ Configuração

### 1️⃣ **Banco de Dados**

Execute o script SQL localizado em `Database/CriarTabelas.sql` no seu SQL Server.

```sql
-- Cria 7 tabelas principais:
- ClienteDelivery
- EnderecoDelivery
- Entregador
- PedidoDelivery
- ItemPedidoDelivery
- AvaliacaoDelivery
- PromocaoDelivery
```

### 2️⃣ **Connection String**

Configure a conexão no `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ConnectionComercial": "Server=SEU_SERVIDOR;Database=SEU_BANCO;User Id=usuario;Password=senha;TrustServerCertificate=True;"
  }
}
```

### 3️⃣ **Executar o Projeto**

```bash
cd EvolutDelivery/EvolutDelivery/EvolutDelivery
dotnet run
```

Acesse: `https://localhost:5001`

---

## 📦 Módulos

### **MÓDULO 1: Gestão de Pedidos**
- Criação de pedidos
- Atualização de status
- Cancelamento de pedidos
- Estatísticas em tempo real
- Histórico completo

### **MÓDULO 2: Gestão de Clientes**
- Cadastro simplificado
- Múltiplos endereços
- Histórico de compras
- Perfil completo

### **MÓDULO 3: Gestão de Entregadores**
- Cadastro de entregadores
- Controle de disponibilidade
- Rastreamento GPS
- Estatísticas de entregas

### **MÓDULO 4: Catálogo de Produtos**
- Produtos com imagens
- Categorias
- Busca e filtros
- Promoções e descontos

### **MÓDULO 5: Pagamentos**
- Dinheiro
- PIX
- Cartão de crédito/débito
- Integração com gateways (futuro)

### **MÓDULO 6: Notificações**
- Notificações em tempo real
- Envio via WhatsApp
- Email (opcional)
- SMS (opcional)

### **MÓDULO 7: Relatórios**
- Vendas por período
- Produtos mais vendidos
- Performance de entregadores
- Satisfação de clientes

---

## 🎨 Páginas Principais

### 🏠 **Home (Cardápio)**
- `/` - Exibe todos os produtos disponíveis
- Busca por nome ou categoria
- Adicionar ao carrinho

### 🛒 **Carrinho**
- `/carrinho` - Visualizar itens selecionados
- Ajustar quantidades
- Finalizar via checkout ou WhatsApp

### ✅ **Checkout**
- `/checkout` - Formulário de finalização
- Dados do cliente e endereço
- Escolha de pagamento
- Confirmação do pedido

### 📦 **Meus Pedidos**
- `/meus-pedidos/{telefone}` - Consultar pedidos
- Rastreamento em tempo real
- Histórico completo

### 🎯 **Admin - Painel de Pedidos**
- `/admin-pedidos` - Gestão completa
- Estatísticas em tempo real
- Controle de status
- Atribuição de entregadores

---

## 📊 Fluxo de Pedido

```
1. PENDENTE       → Cliente faz o pedido
2. CONFIRMADO     → Admin confirma o pedido
3. PREPARANDO     → Cozinha está preparando
4. SAIU_ENTREGA   → Entregador saiu para entregar
5. ENTREGUE       → Pedido foi entregue
```

---

## 🔐 Segurança

- Validação de dados no client e server
- Sanitização de inputs
- Transações de banco de dados
- Proteção contra SQL Injection
- Autenticação (em desenvolvimento)

---

## 🚀 Próximas Funcionalidades

- [ ] Sistema de autenticação JWT
- [ ] App mobile (MAUI)
- [ ] Integração com gateways de pagamento
- [ ] Rastreamento GPS em tempo real
- [ ] Chat entre cliente e entregador
- [ ] Sistema de fidelidade/cupons
- [ ] Integração com iFood/Rappi
- [ ] Dashboard analytics avançado

---

## 📝 Notas Importantes

### **Tabelas Necessárias**
O sistema precisa das seguintes tabelas no banco:
- `ClienteDelivery`
- `EnderecoDelivery`
- `Entregador`
- `PedidoDelivery`
- `ItemPedidoDelivery`
- `AvaliacaoDelivery`
- `PromocaoDelivery`

### **Compatibilidade**
- Funciona com produtos já cadastrados na tabela `Produtos`
- Integra com sistema de vendas existente (`VendaCAFV`, `VendaDAFV`)

---

## 🆘 Suporte

Para dúvidas ou problemas:
1. Verifique se todas as tabelas foram criadas
2. Confira a connection string
3. Certifique-se de que o .NET 8 SDK está instalado
4. Execute `dotnet restore` antes de rodar

---

## 📜 Licença

Este projeto foi desenvolvido pela **EvolutTech** para uso interno.

---

## 👨‍💻 Desenvolvido por

**EvolutTech**  
Sistema completo de gestão empresarial

---

**🎉 Sistema pronto para uso! Boa sorte com seu delivery!** 🚀
