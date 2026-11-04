/****** Object:  Table [evoluttechsistemas].[Produto]    Script Date: 12/07/2023 21:05:42 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[Produto](
	[Codigo] [int] NULL,
	[Descricao] [varchar](150) NULL,
	[PrecoUnitario] [money] NULL,
	[QuantidadeEmbalagem] [money] NULL,
	[CodEmp] [int] NULL,
	[CodSecao] [int] NULL,
	[Estoque] [money] NULL
) ON [PRIMARY];

SET ANSI_PADDING OFF;

/****** Object:  Table [evoluttechsistemas].[Empresa]    Script Date: 12/07/2023 21:06:19 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[Empresa](
	[Codigo] [int] NULL,
	[Razao] [varchar](150) NULL,
	[Fantasia] [varchar](150) NULL,
	[Endereco] [varchar](150) NULL,
	[LojaAberta] [varchar](1) NULL
) ON [PRIMARY];

SET ANSI_PADDING OFF;

/****** Object:  Table [evoluttechsistemas].[Secao]    Script Date: 12/07/2023 21:06:58 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[Secao](
	[Codigo] [int] NULL,
	[Descricao] [varchar](150) NULL
) ON [PRIMARY];

SET ANSI_PADDING OFF;

/****** Object:  Table [evoluttechsistemas].[Complemento]    Script Date: 12/07/2023 21:07:33 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[Complemento](
	[Codigo] [int] NULL,
	[Descricao] [varchar](150) NULL,
	[PrecoUnitario] [money] NULL,
	[CodSecao] [int] NULL
) ON [PRIMARY];

SET ANSI_PADDING OFF;

ALTER TABLE Produto ADD Imagem varbinary(max);

ALTER TABLE Empresa ADD TaxaEntrega money;

ALTER TABLE Empresa ADD Versao int;

/****** Object:  Table [evoluttechsistemas].[Cliente]    Script Date: 12/19/2023 14:17:20 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[Cliente](
	[Codigo] [int] NULL,
	[CPF] [varchar](30) NULL,
	[RG] [varchar](30) NULL,
	[Nome] [varchar](80) NULL,
	[Apelido] [varchar](80) NULL,
	[Endereco] [varchar](80) NULL,
	[Bairro] [varchar](80) NULL,
	[NomeCidade] [varchar](100) NULL,
	[Telefone] [varchar](50) NULL,
	[Email] [varchar](50) NULL,
	[Observacao] [varchar](500) NULL
) ON [PRIMARY];

SET ANSI_PADDING OFF;


/****** Object:  Table [evoluttechsistemas].[Usuario]    Script Date: 12/19/2023 15:48:52 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[Usuario](
	[Codigo] [int] NULL,
	[Usuario] [varchar](50) NULL,
	[Senha] [varchar](50) NULL
) ON [PRIMARY];

SET ANSI_PADDING OFF;

ALTER TABLE Empresa ADD NomeReduzido varchar(20);

/****** Object:  Table [evoluttechsistemas].[ProdutoPreco]    Script Date: 12/20/2023 09:48:07 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [evoluttechsistemas].[ProdutoPreco](
	[Codigo] [int] NULL,
	[CodProduto] [int] NULL,
	[CodEmp] [int] NULL,
	[PrecoCusto] [money] NULL,
	[PrecoVenda] [money] NULL
) ON [PRIMARY];

/****** Object:  Table [evoluttechsistemas].[ProdutoEstoque]    Script Date: 12/20/2023 09:49:50 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

CREATE TABLE [evoluttechsistemas].[ProdutoEstoque](
	[Codigo] [int] NULL,
	[CodProduto] [int] NULL,
	[CodEmp] [int] NULL,
	[EstoqueAtual] [money] NULL
) ON [PRIMARY];

ALTER TABLE Produto DROP COLUMN PrecoUnitario;
ALTER TABLE Produto DROP COLUMN CodEmp;
ALTER TABLE Produto DROP COLUMN Estoque;

ALTER TABLE Produto ADD Unidade varchar(3);

/****** Object:  Table [evoluttechsistemas].[VendaC]    Script Date: 12/20/2023 13:51:46 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[VendaC](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[Status] [varchar](2) NULL,
	[CodCliente] [int] NULL,
	[FormaPagamento] [varchar](3) NULL,
	[EntregaRetira] [varchar](2) NULL,
	[TotalVenda] [money] NULL,
	[CodEmp] [int] NULL,
	[Observacao] [varchar](300) NULL,
 CONSTRAINT [PK_VendaC] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
) ON [PRIMARY];

SET ANSI_PADDING OFF;

/****** Object:  Table [evoluttechsistemas].[VendaD]    Script Date: 12/20/2023 13:56:04 ******/
SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;

SET ANSI_PADDING ON;

CREATE TABLE [evoluttechsistemas].[VendaD](
	[Codigo] [int] IDENTITY(1,1) NOT NULL,
	[CodVendaC] [int] NULL,
	[CodProduto] [int] NULL,
	[NomeProduto] [varchar](150) NULL,
	[Quantidade] [money] NULL,
	[PrecoUnitario] [money] NULL,
	[PrecoTotal] [money] NULL,
	[PrecoCusto] [money] NULL,
 CONSTRAINT [PK_VendaD] PRIMARY KEY CLUSTERED 
(
	[Codigo] ASC
)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
) ON [PRIMARY];

SET ANSI_PADDING OFF;

ALTER TABLE VendaC ADD DataHoraMovimento datetime2;
ALTER TABLE VendaC ADD DataMovimento date;

ALTER TABLE Produto ADD DescricaoConsumer varchar(50);

ALTER TABLE VendaC ADD CodVendedor int;

ALTER TABLE Usuario ADD CodVendedor int;

ALTER TABLE ProdutoPreco ADD PrecoVendaPrazo money;

ALTER TABLE Produto ADD Imagem varbinary(max);

