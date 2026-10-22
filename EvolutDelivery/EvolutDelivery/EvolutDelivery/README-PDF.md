# 📄➡️📕 Conversor de Documentação para PDF

Este diretório contém scripts para converter o arquivo `DOCUMENTACAO.md` para PDF.

## 🚀 Opções de Conversão

### Opção 1: Script Completo (Recomendado) ⭐

**Arquivo:** `ConvertToPdf.ps1`

**Características:**
- ✅ Verifica e instala Pandoc automaticamente
- ✅ Formatação profissional com CSS customizado
- ✅ Índice automático (TOC)
- ✅ Metadados (título, autor, data)
- ✅ Destaque de sintaxe para códigos
- ✅ Paginação e margens otimizadas

**Como usar:**
```powershell
# Executar no PowerShell
.\ConvertToPdf.ps1
```

---

### Opção 2: Script Simples

**Arquivo:** `ConvertToPdf-Simple.ps1`

**Características:**
- ✅ Conversão básica e rápida
- ✅ Requer Pandoc pré-instalado
- ⚠️ Sem formatação avançada

**Como usar:**
```powershell
.\ConvertToPdf-Simple.ps1
```

---

## 📦 Pré-requisitos

### 1. Pandoc (Necessário)

**Instalação Automática:**
O script completo (`ConvertToPdf.ps1`) oferece instalação automática.

**Instalação Manual:**

**Via winget:**
```powershell
winget install --id JohnMacFarlane.Pandoc
```

**Via Chocolatey:**
```powershell
choco install pandoc
```

**Download direto:**
https://pandoc.org/installing.html

---

### 2. wkhtmltopdf (Opcional - Para melhor formatação)

**Via winget:**
```powershell
winget install wkhtmltopdf
```

**Download direto:**
https://wkhtmltopdf.org/downloads.html

---

## 🎯 Uso Rápido

### Passo 1: Abrir PowerShell
```powershell
# Navegar até a pasta do projeto
cd "C:\Users\User\Desktop\desenvolvimentoEvolut\EvolutDelivery\EvolutDelivery\EvolutDelivery"
```

### Passo 2: Permitir Execução de Scripts (Primeira vez)
```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### Passo 3: Executar o Script
```powershell
.\ConvertToPdf.ps1
```

### Passo 4: Aguardar
O script irá:
1. Verificar se Pandoc está instalado
2. Oferecer instalação se necessário
3. Converter o arquivo
4. Gerar `DOCUMENTACAO.pdf`

---

## 🎨 Personalização

### Alterar Nome do Arquivo de Saída

```powershell
.\ConvertToPdf.ps1 -InputFile "DOCUMENTACAO.md" -OutputFile "MeuPDF.pdf"
```

### Modificar Estilo CSS

Edite a seção `$cssContent` no arquivo `ConvertToPdf.ps1` para customizar:
- Fontes
- Cores
- Espaçamentos
- Tamanhos de título

---

## 🔧 Solução de Problemas

### Erro: "Não é possível executar scripts"
```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### Erro: "Pandoc não encontrado"
- Execute o script completo que oferecerá instalar automaticamente
- Ou instale manualmente: `winget install --id JohnMacFarlane.Pandoc`

### PDF sem formatação avançada
- Instale wkhtmltopdf: https://wkhtmltopdf.org/downloads.html
- Reinicie o PowerShell após instalação

### Emojis não aparecem no PDF
- Normal - PDFs convertem emojis para texto
- Solução: Use versões HTML para visualização com emojis

---

## 📊 Alternativas Online (Sem Instalação)

Se não quiser instalar nada, use estas opções:

1. **Dillinger** (Recomendado)
   - https://dillinger.io/
   - Importa MD → Exporta PDF

2. **MarkdownToPDF**
   - https://www.markdowntopdf.com/

3. **CloudConvert**
   - https://cloudconvert.com/md-to-pdf

---

## 📋 Exemplos de Uso

### Conversão Básica
```powershell
.\ConvertToPdf.ps1
```

### Especificar Arquivos
```powershell
.\ConvertToPdf.ps1 -InputFile "README.md" -OutputFile "Manual.pdf"
```

### Verificar Versão do Pandoc
```powershell
pandoc --version
```

---

## 🆘 Suporte

Se tiver problemas:
1. Verifique se Pandoc está instalado: `pandoc --version`
2. Reinicie o PowerShell após instalar ferramentas
3. Execute como Administrador se houver erros de permissão
4. Verifique se o arquivo `DOCUMENTACAO.md` existe na pasta

---

## 📝 Notas

- O script cria um arquivo CSS temporário que é removido após a conversão
- O PDF gerado terá índice clicável se usar o script completo
- Códigos terão destaque de sintaxe automaticamente
- O script detecta e formata tabelas, listas e links automaticamente

---

**Desenvolvido para o projeto EvolutDelivery** 🚀
