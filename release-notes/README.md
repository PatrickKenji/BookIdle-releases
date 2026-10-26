# Notas de versão

Um arquivo por versão: `X.Y.Z.md` (o `tools\release\release.ps1` não lança sem ele).

- O **resumo** aparece no aviso "Atualização disponível", no app de quem ainda está na versão anterior.
- Os **detalhes** aparecem na primeira vez que o app abre na versão nova.

Formato: uma seção por idioma (`# pt`, `# en`, `# es`). Dentro de cada uma, o primeiro `##` é o resumo e o segundo são os detalhes (o nome do subtítulo é livre). Se faltar um idioma, o app usa o português.

```markdown
# pt
## Resumo
- Uma linha por novidade.

## Detalhes
### Nome da novidade
- O que mudou, com mais calma.

# en
## Summary
...
## Details
...

# es
## Resumen
...
## Detalles
...
```

Listas (`-`), subtítulos (`###`) e **negrito** viram texto simples na caixa do app.
