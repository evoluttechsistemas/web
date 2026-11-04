-- ============================================================
--  Status da loja do Delivery (aberta / fechada pelo próprio cliente)
--  Rode uma vez no banco BancoVendas. Pode rodar de novo sem problema.
--  Tabela própria do Delivery: não altera a tabela Empresa do ERP.
-- ============================================================
IF OBJECT_ID('dbo.EmpresaDeliveryStatus', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmpresaDeliveryStatus
    (
        CodEmp          INT            NOT NULL CONSTRAINT PK_EmpresaDeliveryStatus PRIMARY KEY,
        LojaFechada     BIT            NOT NULL CONSTRAINT DF_EmpresaDeliveryStatus_LojaFechada DEFAULT (0),
        MensagemFechada NVARCHAR(200)  NULL,
        AlteradoEm      DATETIME       NOT NULL CONSTRAINT DF_EmpresaDeliveryStatus_AlteradoEm DEFAULT (GETDATE()),
        AlteradoPor     NVARCHAR(100)  NULL
    );
END
