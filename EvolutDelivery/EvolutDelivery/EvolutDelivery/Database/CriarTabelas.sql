-- =====================================================
-- SCRIPT DE CRIAÇÃO DAS TABELAS DO SISTEMA DE DELIVERY
-- EvolutDelivery - Sistema Completo
-- =====================================================

USE [SeuBancoDeDados]
GO

-- =====================================================
-- 1. TABELA DE CLIENTES DO DELIVERY
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ClienteDelivery]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ClienteDelivery] (
        [Codigo] INT IDENTITY(1,1) PRIMARY KEY,
        [Nome] NVARCHAR(200) NOT NULL,
        [Apelido] NVARCHAR(100),
        [Telefone] NVARCHAR(20) NOT NULL,
        [Email] NVARCHAR(200),
        [CPF] NVARCHAR(14),
        [DataCadastro] DATETIME NOT NULL DEFAULT GETDATE(),
        [DataNascimento] DATE,
        [SenhaHash] NVARCHAR(500),
        [Ativo] BIT NOT NULL DEFAULT 1,
        [CodEmp] INT NOT NULL,
        CONSTRAINT UC_Telefone_Cliente UNIQUE (Telefone, CodEmp)
    );
    
    CREATE INDEX IX_ClienteDelivery_Telefone ON ClienteDelivery(Telefone);
    CREATE INDEX IX_ClienteDelivery_Nome ON ClienteDelivery(Nome);
END
GO

-- =====================================================
-- 2. TABELA DE ENDEREÇOS
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[EnderecoDelivery]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[EnderecoDelivery] (
        [Codigo] INT IDENTITY(1,1) PRIMARY KEY,
        [CodCliente] INT NOT NULL,
        [Descricao] NVARCHAR(50) NOT NULL DEFAULT 'Casa',
        [Rua] NVARCHAR(300) NOT NULL,
        [Numero] NVARCHAR(20) NOT NULL,
        [Complemento] NVARCHAR(100),
        [Bairro] NVARCHAR(100) NOT NULL,
        [Cidade] NVARCHAR(100) NOT NULL,
        [Estado] NVARCHAR(2),
        [CEP] NVARCHAR(10) NOT NULL,
        [PontoReferencia] NVARCHAR(200),
        [EnderecoPrincipal] BIT NOT NULL DEFAULT 0,
        [Latitude] NVARCHAR(50),
        [Longitude] NVARCHAR(50),
        FOREIGN KEY (CodCliente) REFERENCES ClienteDelivery(Codigo)
    );
    
    CREATE INDEX IX_EnderecoDelivery_CodCliente ON EnderecoDelivery(CodCliente);
END
GO

-- =====================================================
-- 3. TABELA DE ENTREGADORES
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Entregador]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Entregador] (
        [Codigo] INT IDENTITY(1,1) PRIMARY KEY,
        [Nome] NVARCHAR(200) NOT NULL,
        [Telefone] NVARCHAR(20) NOT NULL,
        [Email] NVARCHAR(200),
        [CPF] NVARCHAR(14),
        [TipoVeiculo] NVARCHAR(20) NOT NULL DEFAULT 'MOTO', -- MOTO, CARRO, BICICLETA, A_PE
        [PlacaVeiculo] NVARCHAR(20),
        [CorVeiculo] NVARCHAR(50),
        [Ativo] BIT NOT NULL DEFAULT 1,
        [Disponivel] BIT NOT NULL DEFAULT 1,
        [StatusAtual] NVARCHAR(20) NOT NULL DEFAULT 'LIVRE', -- LIVRE, EM_ENTREGA, PAUSADO
        [LatitudeAtual] NVARCHAR(50),
        [LongitudeAtual] NVARCHAR(50),
        [UltimaAtualizacaoGPS] DATETIME,
        [TotalEntregas] INT NOT NULL DEFAULT 0,
        [AvaliacaoMedia] DECIMAL(3,2) NOT NULL DEFAULT 0,
        [TotalAvaliacoes] INT NOT NULL DEFAULT 0,
        [DataCadastro] DATETIME NOT NULL DEFAULT GETDATE(),
        [CodEmp] INT NOT NULL
    );
    
    CREATE INDEX IX_Entregador_Status ON Entregador(StatusAtual, Disponivel, Ativo);
END
GO

-- =====================================================
-- 4. TABELA DE PEDIDOS (PRINCIPAL)
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PedidoDelivery]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[PedidoDelivery] (
        [Codigo] INT IDENTITY(1,1) PRIMARY KEY,
        [CodCliente] INT NOT NULL,
        [NomeCliente] NVARCHAR(200) NOT NULL,
        [TelefoneCliente] NVARCHAR(20) NOT NULL,
        [EmailCliente] NVARCHAR(200),
        
        -- Endereço de entrega
        [CodEndereco] INT,
        [EnderecoCompleto] NVARCHAR(500) NOT NULL,
        [Bairro] NVARCHAR(100) NOT NULL,
        [Cidade] NVARCHAR(100) NOT NULL,
        [CEP] NVARCHAR(10) NOT NULL,
        [Complemento] NVARCHAR(100),
        [Numero] NVARCHAR(20) NOT NULL,
        
        -- Valores
        [DataPedido] DATETIME NOT NULL DEFAULT GETDATE(),
        [DataEntrega] DATETIME,
        [ValorSubtotal] DECIMAL(18,2) NOT NULL,
        [ValorEntrega] DECIMAL(18,2) NOT NULL DEFAULT 0,
        [ValorDesconto] DECIMAL(18,2) NOT NULL DEFAULT 0,
        [ValorTotal] DECIMAL(18,2) NOT NULL,
        
        -- Status e controle
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'PENDENTE', -- PENDENTE, CONFIRMADO, PREPARANDO, SAIU_ENTREGA, ENTREGUE, CANCELADO
        [FormaPagamento] NVARCHAR(50) NOT NULL, -- DINHEIRO, PIX, CARTAO, ONLINE
        [TipoEntrega] NVARCHAR(20) NOT NULL DEFAULT 'ENTREGA', -- ENTREGA, RETIRADA
        [Observacao] NVARCHAR(500),
        
        -- Entregador
        [CodEntregador] INT,
        
        -- Rastreamento
        [LatitudeEntrega] NVARCHAR(50),
        [LongitudeEntrega] NVARCHAR(50),
        [UltimaAtualizacaoLocalizacao] DATETIME,
        
        -- Controle
        [Usuario] NVARCHAR(100) NOT NULL DEFAULT 'SITE',
        [CodEmp] INT NOT NULL,
        [Notificado] BIT NOT NULL DEFAULT 0,
        
        FOREIGN KEY (CodCliente) REFERENCES ClienteDelivery(Codigo),
        FOREIGN KEY (CodEntregador) REFERENCES Entregador(Codigo)
    );
    
    CREATE INDEX IX_PedidoDelivery_Status ON PedidoDelivery(Status, DataPedido);
    CREATE INDEX IX_PedidoDelivery_Cliente ON PedidoDelivery(CodCliente);
    CREATE INDEX IX_PedidoDelivery_Data ON PedidoDelivery(DataPedido DESC);
    CREATE INDEX IX_PedidoDelivery_Telefone ON PedidoDelivery(TelefoneCliente);
END
GO

-- =====================================================
-- 5. TABELA DE ITENS DO PEDIDO
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ItemPedidoDelivery]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ItemPedidoDelivery] (
        [Codigo] INT IDENTITY(1,1) PRIMARY KEY,
        [CodPedido] INT NOT NULL,
        [CodProduto] INT NOT NULL,
        [NomeProduto] NVARCHAR(200) NOT NULL,
        [PrecoUnitario] DECIMAL(18,2) NOT NULL,
        [Quantidade] INT NOT NULL,
        [PrecoTotal] DECIMAL(18,2) NOT NULL,
        [Observacao] NVARCHAR(200),
        FOREIGN KEY (CodPedido) REFERENCES PedidoDelivery(Codigo) ON DELETE CASCADE
    );
    
    CREATE INDEX IX_ItemPedidoDelivery_Pedido ON ItemPedidoDelivery(CodPedido);
    CREATE INDEX IX_ItemPedidoDelivery_Produto ON ItemPedidoDelivery(CodProduto);
END
GO

-- =====================================================
-- 6. TABELA DE AVALIAÇÕES
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AvaliacaoDelivery]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AvaliacaoDelivery] (
        [Codigo] INT IDENTITY(1,1) PRIMARY KEY,
        [CodPedido] INT NOT NULL,
        [CodCliente] INT NOT NULL,
        [NomeCliente] NVARCHAR(200) NOT NULL,
        [Estrelas] INT NOT NULL CHECK (Estrelas BETWEEN 1 AND 5),
        [Comentario] NVARCHAR(500),
        [DataAvaliacao] DATETIME NOT NULL DEFAULT GETDATE(),
        [CodProduto] INT,
        [CodEntregador] INT,
        FOREIGN KEY (CodPedido) REFERENCES PedidoDelivery(Codigo),
        FOREIGN KEY (CodCliente) REFERENCES ClienteDelivery(Codigo),
        FOREIGN KEY (CodEntregador) REFERENCES Entregador(Codigo)
    );
    
    CREATE INDEX IX_AvaliacaoDelivery_Pedido ON AvaliacaoDelivery(CodPedido);
    CREATE INDEX IX_AvaliacaoDelivery_Entregador ON AvaliacaoDelivery(CodEntregador);
END
GO

-- =====================================================
-- 7. TABELA DE PROMOÇÕES
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PromocaoDelivery]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[PromocaoDelivery] (
        [Codigo] INT IDENTITY(1,1) PRIMARY KEY,
        [Nome] NVARCHAR(200) NOT NULL,
        [Descricao] NVARCHAR(500),
        [TipoDesconto] NVARCHAR(20) NOT NULL DEFAULT 'PERCENTUAL', -- PERCENTUAL, VALOR_FIXO
        [ValorDesconto] DECIMAL(18,2) NOT NULL,
        [DataInicio] DATETIME NOT NULL,
        [DataFim] DATETIME NOT NULL,
        [Ativo] BIT NOT NULL DEFAULT 1,
        [CodProduto] INT,
        [CodEmp] INT NOT NULL
    );
    
    CREATE INDEX IX_PromocaoDelivery_Ativo ON PromocaoDelivery(Ativo, DataInicio, DataFim);
END
GO

-- =====================================================
-- 8. INSERIR DADOS DE EXEMPLO (OPCIONAL)
-- =====================================================
PRINT 'Tabelas criadas com sucesso!';
PRINT 'Total de tabelas: 7';
PRINT '';
PRINT 'Tabelas criadas:';
PRINT '  1. ClienteDelivery';
PRINT '  2. EnderecoDelivery';
PRINT '  3. Entregador';
PRINT '  4. PedidoDelivery';
PRINT '  5. ItemPedidoDelivery';
PRINT '  6. AvaliacaoDelivery';
PRINT '  7. PromocaoDelivery';
PRINT '';
PRINT '✅ Sistema de Delivery - Estrutura completa instalada!';
GO
