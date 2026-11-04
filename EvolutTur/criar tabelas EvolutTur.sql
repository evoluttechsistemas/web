use EvolutTur;


/****** Object:  Table [dbo].[Cliente]    Script Date: 31/07/2025 13:21:41 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [dbo].[Cliente](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[Nome] [varchar](150) NULL,
	[Endereco] [varchar](50) NULL,
	[Bairro] [varchar](50) NULL,
	[Cidade] [varchar](50) NULL,
	[Numero] [varchar](50) NULL,
	[CPF] [varchar](15) NULL,
	[Telefone] [varchar](50) NULL,
	[Email] [varchar](50) NULL,
	[CodEmp] [int] NULL,
 CONSTRAINT [PK_Cliente] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];



/****** Object:  Table [dbo].[Fornecedor]    Script Date: 02/08/2025 16:30:15 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [dbo].[Fornecedor](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[Razao] [varchar](150) NULL,
	[Fantasia] [varchar](150) NULL,
	[TipoPessoa] [varchar](10) NULL,
	[CPF] [varchar](20) NULL,
	[RG] [varchar](20) NULL,
	[CEP] [varchar](20) NULL,
	[UF] [varchar](2) NULL,
	[Endereco] [varchar](50) NULL,
	[Numero] [varchar](20) NULL,
	[Bairro] [varchar](50) NULL,
	[Cidade] [varchar](50) NULL,
	[Complemento] [varchar](150) NULL,
	[Telefone] [varchar](30) NULL,
	[Email] [varchar](100) NULL,
	[Comissao] [money] NULL,
	[Observacao] [varchar](300) NULL,
	[CodEmp] [int] NULL,
 CONSTRAINT [PK_Fornecedor] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];



/****** Object:  Table [dbo].[Vendedor]    Script Date: 02/08/2025 16:30:36 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [dbo].[Vendedor](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[Nome] [varchar](150) NULL,
	[Comissao] [money] NULL,
	[CodEmp] [int] NULL,
 CONSTRAINT [PK_Vendedor] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];


/****** Object:  Table [dbo].[VendaC]    Script Date: 08/09/2025 10:45:19 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [dbo].[VendaC](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[CodEmp] [int] NULL,
	[CodFornecedor] [int] NULL,
	[CodVendedor] [int] NULL,
	[DataVenda] [date] NULL,
	[CodCliente] [int] NULL,
	[OrcamentoAtendimento] [int] NULL,
	[DataHoraVenda] [datetime2](7) NULL,
	[Usuario] [varchar](50) NULL,
 CONSTRAINT [PK_VendaC] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];



CREATE TABLE [dbo].[VendaD]( 
[Codigo] [int] IDENTITY(1,1) NOT NULL, [CodVendaC] [int] NULL, [Tipo] [varchar](50) NULL, [CompanhiaAerea] [varchar](150) NULL, [Localizador] [varchar](150) NULL, [NumeroVoo] [int] NULL, [Origem] [varchar](150) NULL, [Destino] [varchar](150) NULL, [DataVoo] [date] NULL, [Valor] [money] NULL, [Hotel] [varchar](150) NULL, [Reserva] [varchar](50) NULL, [Checkin] [date] NULL, [Checkout] [date] NULL, [NumeroQuartos] [int] NULL, [NomePacote] [varchar](150) NULL, [Transporte] [varchar](150) NULL, [Emissao] [date] NULL, [ServicoIncluso] [varchar](150) NULL, [Seguro] [varchar](150) NULL, [Inicio] [date] NULL, [Fim] [date] NULL, [CategoriaCarro] [varchar](150) NULL, [LocalRetirada] [varchar](150) NULL, [LocalDevolucao] [varchar](150) NULL, [DataHoraRetirada] [datetime2](7) NULL, [DataHoraDevolucao] [datetime2](7) NULL, [Descricao] [varchar](150) NULL, [Companhia] [varchar](150) NULL, [Classe] [varchar](50) NULL, [DataHoraSaida] [datetime2](7) NULL, [DataHoraChegada] [datetime2](7) NULL, [Navio] [varchar](150) NULL, [NumeroCabine] [varchar](50) NULL, [TipoCabine] [varchar](50) NULL, [Acomodacao] [varchar](50) NULL, [Regime] [varchar](50) NULL, [NomePasseio] [varchar](150) NULL, [NomeIngresso] [varchar](150) NULL, [DataUtilizacao] [date] NULL, [TipoProduto] [varchar](150) NULL ) ON [PRIMARY];


/****** Object:  Table [dbo].[ContaReceber]    Script Date: 08/09/2025 10:57:56 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [dbo].[ContaReceber](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[CodVendaC] [int] NULL,
	[Tipo] [varchar](150) NULL,
	[NumeroCartao] [int] NULL,
	[NomeCartao] [varchar](150) NULL,
	[Validade] [varchar](50) NULL,
	[CVV] [varchar](50) NULL,
	[Parcelas] [varchar](10) NULL,
	[DataVencimento] [date] NULL,
	[ChavePIX] [varchar](50) NULL,
 CONSTRAINT [PK_ContaReceber] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];


/****** Object:  Table [dbo].[VendaViajante]    Script Date: 08/09/2025 11:16:17 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [dbo].[VendaViajante](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[CodVendaC] [int] NULL,
	[Nome] [varchar](150) NULL,
	[Sobrenome] [varchar](150) NULL,
	[CPF] [varchar](50) NULL,
	[DataNascimento] [date] NULL,
 CONSTRAINT [PK_VendaViajante] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];


ALTER TABLE VendaD ALTER COLUMN NumeroVoo VARCHAR(50);

ALTER TABLE VendaC add TotalVenda money;
ALTER TABLE ContaReceber add Valor money;

ALTER TABLE VendaC add PercentualComissaoFornecedor money;
ALTER TABLE VendaC add TotalComissaoFornecedor money;

ALTER TABLE VendaC add PercentualComissaoVendedor money;
ALTER TABLE VendaC add TotalComissaoVendedor money;


/****** Object:  Table [dbo].[Parametros]    Script Date: 11/09/2025 17:19:32 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [dbo].[Parametros](
	[CodEmp] [int] NULL,
	[NomeAgencia] [varchar](150) NULL,
	[CNPJ] [varchar](25) NULL,
	[Endereco] [varchar](250) NULL,
	[Telefone] [varchar](20) NULL,
	[Email] [varchar](30) NULL,
	[Contrato] [varchar](max) NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];