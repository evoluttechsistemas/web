namespace EvolutCRM.Blog;

public static partial class LayoutRenderer
{
    // Folha de estilo do layout. Tudo prefixado com .evl- e baseado em variáveis
    // (--evl-* do tema e --s-* de cada bloco). Usa container queries, então o
    // preview "celular/tablet" do editor responde igual ao site.
    public const string Css = """
.evl-root{container:evl/inline-size;background:var(--evl-bg);color:var(--evl-text);font-family:var(--evl-font-body);font-size:var(--evl-fs);line-height:var(--evl-lh);-webkit-font-smoothing:antialiased;text-rendering:optimizeLegibility;overflow-x:clip;display:flex;flex-direction:column;gap:var(--evl-gap);min-height:100%}
.evl-root *,.evl-root *::before,.evl-root *::after{box-sizing:border-box}
.evl-root h1,.evl-root h2,.evl-root h3,.evl-root h4{font-family:var(--evl-font-head);font-weight:var(--evl-hw);color:var(--s-head,var(--evl-heading));line-height:1.18;letter-spacing:-.015em;margin:0}
.evl-root p{margin:0}
.evl-root a{color:var(--s-link,var(--evl-link))}
.evl-root img{max-width:100%}
.evl-progress{position:sticky;top:0;height:4px;margin-bottom:-4px;z-index:40;pointer-events:none}
.evl-progress span{display:block;height:100%;background:linear-gradient(90deg,var(--evl-primary),var(--evl-secondary));transform-origin:0 50%;transform:scaleX(0)}
@supports (animation-timeline: scroll()){.evl-progress span{animation:evl-prog linear both;animation-timeline:scroll(nearest block)}}
@keyframes evl-prog{to{transform:scaleX(1)}}
.evl-s{position:relative;color:var(--s-text,var(--evl-text))}
.evl-fmt-faixa{overflow:hidden}
.evl-fmt-cartao{padding:12px 16px}
.evl-box{position:relative;isolation:isolate;margin:0 auto;overflow:hidden;color:var(--s-text,var(--evl-text))}
.evl-w{position:relative;z-index:2;margin:0 auto;width:100%}
.evl-hbox{display:flex;align-items:center}
.evl-bgimg{position:absolute;inset:0;z-index:0;background-size:cover;background-position:center}
.evl-overlay{position:absolute;inset:0;z-index:1}
.evl-div{position:absolute;left:0;right:0;height:64px;z-index:1;line-height:0;pointer-events:none}
.evl-div svg{width:100%;height:100%;display:block}
.evl-div-top{top:-1px}
.evl-div-bot{bottom:-1px;transform:scaleY(-1)}
.evl-left{text-align:left}.evl-center{text-align:center}.evl-right{text-align:right}
.evl-eyebrow{display:inline-flex;align-items:center;gap:.6em;font-size:.76em;font-weight:800;letter-spacing:.1em;text-transform:uppercase;color:var(--s-accent);margin-bottom:1em;font-family:var(--evl-font-head)}
.evl-eyebrow::before{content:"";width:24px;height:2px;border-radius:2px;background:currentColor}
.evl-t{font-size:calc(1.85em * var(--s-scale,1));margin-bottom:.55em}
.evl-lead{font-size:1.08em;opacity:.9;max-width:64ch}
.evl-center .evl-lead{margin-left:auto;margin-right:auto}
.evl-right .evl-lead{margin-left:auto}
.evl-rich p+p,.evl-rich p+ul,.evl-rich ul+p,.evl-rich p+ol,.evl-rich ol+p{margin-top:.9em}
.evl-rich ul,.evl-rich ol{margin:.6em 0;padding-left:1.3em;text-align:left}
.evl-center .evl-rich ul,.evl-center .evl-rich ol{display:inline-block}
.evl-rich li{margin:.3em 0}
.evl-rich li::marker{color:var(--s-accent)}
.evl-rich mark{background:color-mix(in srgb,var(--s-accent) 22%,transparent);color:inherit;padding:0 .2em;border-radius:3px}
.evl-rich a{color:var(--s-accent);text-underline-offset:3px}
.evl-btns{display:flex;flex-wrap:wrap;gap:12px;margin-top:1.7em}
.evl-center .evl-btns{justify-content:center}
.evl-right .evl-btns{justify-content:flex-end}
.evl-btn{display:inline-flex;align-items:center;gap:.5em;padding:.85em 1.6em;border-radius:calc(var(--s-radius) * .75);font-weight:700;font-size:.95em;text-decoration:none!important;transition:transform .15s,box-shadow .15s,background .15s;border:2px solid transparent;line-height:1.2;font-family:var(--evl-font-head)}
.evl-btn:hover{transform:translateY(-2px)}
.evl-btn-solido{background:var(--s-accent);color:var(--s-on-accent)!important;box-shadow:0 8px 22px color-mix(in srgb,var(--s-accent) 30%,transparent)}
.evl-btn-contorno{border-color:var(--s-accent);color:var(--s-accent)!important;background:transparent}
.evl-btn-claro{background:color-mix(in srgb,var(--s-accent) 13%,transparent);color:var(--s-accent)!important}
.evl-btn-link{padding-left:0;padding-right:0;color:var(--s-accent)!important;border:0}
.evl-btn-link::after{content:"→";transition:transform .15s}
.evl-btn-link:hover{transform:none}.evl-btn-link:hover::after{transform:translateX(4px)}
.evl-t-hero .evl-t{font-size:calc(clamp(2em,4.6cqi,3em) * var(--s-scale,1));line-height:1.07;letter-spacing:-.028em}
.evl-t-hero .evl-lead{font-size:1.15em}
.evl-meta{display:flex;flex-wrap:wrap;gap:.5em 1.4em;margin-top:1.5em;font-size:.86em;opacity:.82;font-weight:600}
.evl-center .evl-meta{justify-content:center}.evl-right .evl-meta{justify-content:flex-end}
.evl-meta span{display:inline-flex;align-items:center;gap:.45em}
.evl-split{display:grid;grid-template-columns:1.1fr .9fr;gap:52px;align-items:center}
.evl-split.evl-img-esquerda{grid-template-columns:.9fr 1.1fr}
.evl-split.evl-img-esquerda .evl-media{order:-1}
.evl-media img{width:100%;display:block;border-radius:var(--s-radius);box-shadow:var(--s-shadow)}
.evl-stack .evl-media{margin-bottom:1.8em}
.evl-content{overflow-wrap:break-word;text-align:left}
.evl-content.evl-card{background:var(--evl-surface);border-radius:var(--s-radius);box-shadow:var(--s-shadow);padding:clamp(22px,5cqi,56px)}
.evl-content>*:first-child{margin-top:0}
.evl-content p{margin:0 0 1.1em}
.evl-content h1{font-size:1.9em;margin:1.4em 0 .5em}
.evl-content h2{font-size:1.5em;margin:1.8em 0 .65em;scroll-margin-top:24px}
.evl-content h3{font-size:1.2em;margin:1.5em 0 .5em;scroll-margin-top:24px}
.evl-content h4{font-size:1.05em;margin:1.3em 0 .45em}
.evl-escrita>*:last-child{margin-bottom:0}
.evl-escrita img{display:block;margin:1.3em auto;box-shadow:var(--s-shadow)}
.evl-escrita p:empty{min-height:1em}
.evl-h2-linha .evl-content h2{padding-bottom:.35em;border-bottom:2px solid color-mix(in srgb,var(--evl-primary) 20%,transparent)}
.evl-h2-barra .evl-content h2{padding-left:.7em;border-left:4px solid var(--evl-primary)}
.evl-h2-marcador .evl-content h2::before{content:"";display:inline-block;width:.55em;height:.55em;border-radius:3px;background:linear-gradient(135deg,var(--evl-primary),var(--evl-secondary));margin-right:.55em;vertical-align:.12em}
.evl-content a{color:var(--evl-link);text-decoration:underline;text-underline-offset:3px}
.evl-content ul,.evl-content ol{margin:0 0 1.1em;padding-left:1.4em}
.evl-content li{margin:.35em 0}
.evl-content li::marker{color:var(--evl-primary)}
.evl-content blockquote{margin:1.5em 0;padding:1em 1.4em;border-left:4px solid var(--evl-primary);background:var(--evl-soft);border-radius:0 var(--s-radius) var(--s-radius) 0;font-style:italic;color:var(--evl-heading)}
.evl-content pre{background:#0f172a;color:#e2e8f0;border-radius:calc(var(--s-radius) * .8);padding:1.1em 1.3em;overflow-x:auto;font-size:.88em;margin:1.3em 0;line-height:1.6}
.evl-content code{font-family:'IBM Plex Mono',ui-monospace,monospace;font-size:.88em;background:var(--evl-soft);color:var(--evl-primary);padding:.12em .4em;border-radius:5px}
.evl-content pre code{background:none;color:inherit;padding:0}
.evl-content hr{border:none;height:2px;background:linear-gradient(90deg,transparent,color-mix(in srgb,var(--evl-primary) 40%,transparent),transparent);margin:2.2em 0}
.evl-content mark{background:#fef08a;color:#713f12;border-radius:3px;padding:0 .2em}
.evl-content img{max-width:100%;height:auto;border-radius:calc(var(--s-radius) * .8)}
.evl-content .be-img-wrap{display:inline-block;max-width:100%;margin:.8em 0;line-height:0;position:relative}
.evl-content .be-img-controls,.evl-content .be-resize-handle,.evl-content .be-video-controls{display:none!important}
.evl-content .be-video-wrap{position:relative;border-radius:var(--s-radius);overflow:hidden;margin:1.4em 0;background:#000;box-shadow:var(--s-shadow)}
.evl-content .be-video-wrap iframe{width:100%;aspect-ratio:16/9;border:0;display:block}
.evl-content .be-video-wrap video{width:100%;display:block}
.evl-content .be-callout{display:flex;gap:.8em;align-items:flex-start;padding:1em 1.2em;border-radius:var(--s-radius);margin:1.3em 0;background:var(--evl-soft);border:1px solid color-mix(in srgb,var(--evl-primary) 22%,transparent)}
.evl-content .be-callout[data-type=warning]{background:#fffbeb;border-color:#fde68a;color:#78350f}
.evl-content .be-callout[data-type=danger]{background:#fef2f2;border-color:#fecaca;color:#7f1d1d}
.evl-content .be-callout[data-type=success]{background:#f0fdf4;border-color:#bbf7d0;color:#14532d}
.evl-content .be-callout-icon{font-size:1.3em;line-height:1.3}
.evl-content .be-table-wrap{overflow-x:auto;margin:1.3em 0;border-radius:calc(var(--s-radius) * .7);border:1px solid color-mix(in srgb,var(--evl-text) 12%,transparent)}
.evl-content table{border-collapse:collapse;width:100%;font-size:.93em}
.evl-content th{background:var(--evl-primary);color:var(--evl-on-primary);text-align:left;padding:.7em 1em;font-weight:700}
.evl-content td{padding:.65em 1em;border-top:1px solid color-mix(in srgb,var(--evl-text) 10%,transparent)}
.evl-content tr:nth-child(even) td{background:color-mix(in srgb,var(--evl-primary) 4%,transparent)}
.evl-dropcap>p:first-of-type::first-letter{float:left;font-family:var(--evl-font-head);font-size:3.7em;line-height:.85;font-weight:800;margin:.06em .12em 0 0;color:var(--evl-primary)}
.evl-vazio{border:2px dashed color-mix(in srgb,var(--evl-text) 22%,transparent);border-radius:var(--s-radius);padding:2.4em 1.5em;text-align:center;opacity:.7;font-size:.95em}
.evl-toc{background:var(--evl-surface);border:1px solid color-mix(in srgb,var(--evl-text) 10%,transparent);border-radius:var(--s-radius);padding:1.1em 1.4em;margin:0 0 2.2em;text-align:left}
.evl-toc-t{font-size:.72em;font-weight:800;letter-spacing:.12em;text-transform:uppercase;color:var(--evl-primary);margin-bottom:.6em;font-family:var(--evl-font-head)}
.evl-toc ul{margin:0;padding:0;list-style:none;counter-reset:evltoc}
.evl-toc li{margin:.35em 0;font-size:.93em;counter-increment:evltoc;display:flex;gap:.6em}
.evl-toc li::before{content:counter(evltoc,decimal-leading-zero);font-weight:800;color:var(--evl-primary);font-size:.85em;min-width:1.6em;padding-top:.08em}
.evl-toc li.evl-n3{margin-left:2.2em;counter-increment:none;font-size:.88em}
.evl-toc li.evl-n3::before{content:"–";min-width:auto}
.evl-toc a{color:var(--evl-text);text-decoration:none}
.evl-toc a:hover{color:var(--evl-primary)}
.evl-cols{display:grid;grid-template-columns:repeat(var(--cols,2),minmax(0,1fr));gap:36px;margin-top:.6em}
.evl-grid{display:grid;grid-template-columns:repeat(var(--cols,3),minmax(0,1fr));gap:20px;margin-top:1.9em}
.evl-card-i{background:var(--evl-surface);color:var(--evl-text);border-radius:var(--s-radius);padding:1.5em;box-shadow:var(--s-shadow);transition:transform .2s,box-shadow .2s;text-align:left;display:flex;flex-direction:column;gap:.45em;overflow:hidden}
.evl-card-i:hover{transform:translateY(-4px)}
.evl-card-i h3{font-size:1.08em;color:var(--evl-heading)}
.evl-card-i p{font-size:.93em;opacity:.85}
.evl-card-i>img{width:calc(100% + 3em);max-width:none;margin:-1.5em -1.5em .7em;aspect-ratio:16/9;object-fit:cover}
.evl-card-i .evl-btn{margin-top:auto;padding-top:.5em;font-size:.9em}
.evl-v-contorno .evl-card-i{background:transparent;box-shadow:none;border:1.5px solid color-mix(in srgb,currentColor 16%,transparent);color:inherit}
.evl-v-simples .evl-card-i{background:transparent;box-shadow:none;padding:0;color:inherit}
.evl-v-simples .evl-card-i>img{width:100%;margin:0 0 .7em;border-radius:var(--s-radius)}
.evl-v-contorno .evl-card-i h3,.evl-v-simples .evl-card-i h3{color:var(--s-head,var(--evl-heading))}
.evl-ico{width:50px;height:50px;border-radius:calc(var(--s-radius) * .8);display:grid;place-items:center;font-size:1.45em;background:color-mix(in srgb,var(--s-accent) 13%,transparent);margin-bottom:.35em;flex-shrink:0}
.evl-center .evl-card-i{text-align:center;align-items:center}
.evl-num{text-align:center;padding:1.3em .6em}
.evl-num strong{display:block;font-family:var(--evl-font-head);font-size:2.7em;font-weight:800;line-height:1;letter-spacing:-.03em;background:linear-gradient(135deg,var(--s-accent),var(--evl-secondary));-webkit-background-clip:text;background-clip:text;color:transparent}
.evl-num span{display:block;margin-top:.55em;font-weight:700;color:var(--s-head,var(--evl-heading))}
.evl-num small{display:block;margin-top:.25em;font-size:.85em;opacity:.72}
.evl-v-cartao .evl-num{background:var(--evl-surface);border-radius:var(--s-radius);box-shadow:var(--s-shadow);color:var(--evl-text)}
.evl-v-cartao .evl-num span{color:var(--evl-heading)}
.evl-v-divisoria .evl-num+.evl-num{border-left:1px solid color-mix(in srgb,currentColor 15%,transparent)}
.evl-steps{list-style:none;margin:1.9em 0 0;padding:0;display:flex;flex-direction:column;counter-reset:evlstep;text-align:left}
.evl-step{position:relative;padding:0 0 1.9em 4.1em;counter-increment:evlstep}
.evl-step::before{content:counter(evlstep);position:absolute;left:0;top:0;width:2.6em;height:2.6em;border-radius:50%;display:grid;place-items:center;font-weight:800;background:var(--s-accent);color:var(--s-on-accent);font-family:var(--evl-font-head);box-shadow:0 0 0 6px color-mix(in srgb,var(--s-accent) 16%,transparent)}
.evl-step::after{content:"";position:absolute;left:1.3em;top:3em;bottom:.3em;width:2px;transform:translateX(-1px);background:color-mix(in srgb,var(--s-accent) 28%,transparent)}
.evl-step:last-child{padding-bottom:0}.evl-step:last-child::after{display:none}
.evl-step h3{font-size:1.12em;margin:.4em 0 .3em}
.evl-step p{opacity:.86}
.evl-v-horizontal .evl-steps{flex-direction:row;gap:28px}
.evl-v-horizontal .evl-step{flex:1;padding:3.9em 0 0}
.evl-v-horizontal .evl-step::after{left:3.3em;right:-28px;top:1.3em;bottom:auto;width:auto;height:2px;transform:none}
.evl-faq{margin-top:1.7em;display:flex;flex-direction:column;gap:10px;text-align:left}
.evl-faq details{background:var(--evl-surface);color:var(--evl-text);border-radius:calc(var(--s-radius) * .8);box-shadow:var(--s-shadow);border:1px solid color-mix(in srgb,var(--evl-text) 8%,transparent);overflow:hidden}
.evl-faq summary{cursor:pointer;list-style:none;padding:1.05em 3.2em 1.05em 1.3em;font-weight:700;position:relative;color:var(--evl-heading)}
.evl-faq summary::-webkit-details-marker{display:none}
.evl-faq summary::after{content:"+";position:absolute;right:1.1em;top:50%;transform:translateY(-50%);font-size:1.5em;color:var(--s-accent);transition:transform .2s;font-weight:400;line-height:1}
.evl-faq details[open] summary::after{transform:translateY(-50%) rotate(45deg)}
.evl-faq-a{padding:0 1.3em 1.15em;opacity:.88}
.evl-check{list-style:none;padding:0;margin:1.5em 0 0;display:grid;grid-template-columns:repeat(var(--cols,1),minmax(0,1fr));gap:.9em 2em;text-align:left}
.evl-center .evl-check{max-width:720px;margin-left:auto;margin-right:auto}
.evl-check li{display:flex;gap:.85em;align-items:flex-start}
.evl-ck{flex-shrink:0;width:1.7em;height:1.7em;border-radius:50%;display:grid;place-items:center;font-size:.78em;font-weight:800;background:var(--s-accent);color:var(--s-on-accent);margin-top:.2em}
.evl-check strong{display:block;color:var(--s-head,var(--evl-heading))}
.evl-check p{opacity:.85;font-size:.95em}
.evl-quote{position:relative;margin:0}
.evl-quote blockquote{margin:0;font-family:var(--evl-font-head);font-size:1.5em;line-height:1.45;font-weight:600;color:var(--s-head,var(--evl-heading));letter-spacing:-.01em}
.evl-qmark{font-family:Georgia,serif;font-size:5.4em;line-height:.62;color:var(--s-accent);opacity:.35;display:block;height:.48em}
.evl-quote figcaption{display:flex;align-items:center;gap:.85em;margin-top:1.4em;font-size:.95em}
.evl-center .evl-quote figcaption{justify-content:center}.evl-right .evl-quote figcaption{justify-content:flex-end}
.evl-quote figcaption img{width:50px;height:50px;border-radius:50%;object-fit:cover}
.evl-quote figcaption b{display:block;color:var(--s-head,var(--evl-heading))}
.evl-quote figcaption small{opacity:.72}
.evl-v-destaque .evl-quote{padding:2.1em 2.3em;background:var(--evl-surface);border-radius:var(--s-radius);box-shadow:var(--s-shadow);border-left:5px solid var(--s-accent)}
.evl-v-destaque .evl-quote blockquote{color:var(--evl-heading)}
.evl-v-minimal .evl-qmark{display:none}
.evl-v-minimal .evl-quote blockquote{font-style:italic;font-weight:400;font-size:1.25em;border-left:3px solid var(--s-accent);padding-left:1em}
.evl-callout{display:flex;gap:1em;align-items:flex-start;padding:1.35em 1.55em;border-radius:var(--s-radius);background:var(--cb-bg);color:var(--cb-fg);border:1px solid var(--cb-bd);border-left:5px solid var(--cb-ac);text-align:left}
.evl-cico{font-size:1.6em;line-height:1.1;flex-shrink:0}
.evl-callout h3{font-size:1.06em;color:var(--cb-fg);margin-bottom:.3em}
.evl-callout .evl-rich{font-size:.97em}
.evl-callout .evl-rich li::marker,.evl-callout .evl-rich a{color:var(--cb-ac)}
.evl-fig{margin:0}
.evl-fig img{width:100%;display:block;border-radius:var(--s-radius)}
.evl-v-sombra .evl-fig img{box-shadow:var(--s-shadow)}
.evl-v-moldura .evl-fig img{padding:10px;background:var(--evl-surface);box-shadow:var(--s-shadow)}
.evl-v-reta .evl-fig img{border-radius:0}
.evl-fig figcaption{margin-top:.85em;font-size:.86em;opacity:.72;text-align:center}
.evl-gal{display:grid;grid-template-columns:repeat(var(--cols,3),minmax(0,1fr));gap:12px;margin-top:1.5em}
.evl-gal figure{margin:0;position:relative;overflow:hidden;border-radius:calc(var(--s-radius) * .8);background:color-mix(in srgb,var(--evl-text) 8%,transparent)}
.evl-gal img{width:100%;aspect-ratio:4/3;object-fit:cover;display:block;transition:transform .45s}
.evl-gal figure:hover img{transform:scale(1.06)}
.evl-gal figcaption{position:absolute;left:0;right:0;bottom:0;padding:1.8em .9em .65em;background:linear-gradient(transparent,rgba(0,0,0,.68));color:#fff;font-size:.82em;font-weight:600;text-align:left}
.evl-video{position:relative;border-radius:var(--s-radius);overflow:hidden;box-shadow:var(--s-shadow);background:#000;margin-top:.4em}
.evl-video iframe,.evl-video video{width:100%;aspect-ratio:16/9;border:0;display:block}
.evl-author{display:flex;gap:1.5em;align-items:center;text-align:left}
.evl-author>img,.evl-av{width:92px;height:92px;border-radius:50%;object-fit:cover;flex-shrink:0;box-shadow:0 0 0 4px var(--evl-surface),0 0 0 6px color-mix(in srgb,var(--s-accent) 45%,transparent)}
.evl-av{display:grid;place-items:center;font-size:1.9em;font-weight:800;color:#fff;background:linear-gradient(135deg,var(--s-accent),var(--evl-secondary));font-family:var(--evl-font-head)}
.evl-author small{display:block;font-size:.72em;font-weight:800;letter-spacing:.12em;text-transform:uppercase;color:var(--s-accent)}
.evl-author h3{font-size:1.3em;margin:.15em 0 .1em}
.evl-role{font-size:.9em;opacity:.72;margin-bottom:.55em}
.evl-author .evl-btns{margin-top:1em}
.evl-center .evl-author{flex-direction:column;text-align:center}
.evl-hr{border:0;margin:0 auto;height:0;border-top:var(--hr-w,2px) var(--hr-s,solid) var(--hr-c)}
.evl-hr-duplo{border-top:4px double var(--hr-c)}
.evl-hr-degrade{height:2px;border:0;background:linear-gradient(90deg,transparent,var(--hr-c),transparent)}
.evl-hr-icone{display:flex;align-items:center;gap:1em;color:var(--hr-c)}
.evl-hr-icone::before,.evl-hr-icone::after{content:"";flex:1;height:1.5px;background:currentColor}
.evl-hr-icone span{font-size:1.3em;line-height:1}
.evl-hr-pontos{text-align:center;letter-spacing:.9em;color:var(--hr-c);font-size:1.5em;line-height:1;padding-left:.9em}
.evl-t-banner .evl-t{font-size:calc(2em * var(--s-scale,1))}
.evl-t-cta .evl-t{font-size:calc(2.1em * var(--s-scale,1))}
@container evl (max-width: 760px){
.evl-split,.evl-cols{grid-template-columns:1fr!important;gap:28px}
.evl-split.evl-img-esquerda .evl-media{order:0}
.evl-grid,.evl-gal{grid-template-columns:repeat(2,minmax(0,1fr))}
.evl-check{grid-template-columns:1fr}
.evl-v-horizontal .evl-steps{flex-direction:column;gap:0}
.evl-v-horizontal .evl-step{padding:0 0 1.9em 4.1em}
.evl-v-horizontal .evl-step::after{left:1.3em;right:auto;top:3em;bottom:.3em;width:2px;height:auto;transform:translateX(-1px)}
.evl-v-divisoria .evl-num+.evl-num{border-left:0}
.evl-hide-m{display:none!important}
.evl-div{height:36px}
.evl-t-hero .evl-t{font-size:calc(2.1em * var(--s-scale,1))}
.evl-author{flex-direction:column;text-align:center}
.evl-quote blockquote{font-size:1.25em}
.evl-h-grande,.evl-h-tela{min-height:420px!important}
}
@container evl (max-width: 480px){
.evl-grid,.evl-gal{grid-template-columns:1fr}
.evl-t{font-size:calc(1.5em * var(--s-scale,1))}
.evl-btns .evl-btn{flex:1;justify-content:center}
}
@container evl (min-width: 761px){.evl-hide-d{display:none!important}}
@keyframes evl-in-fade{from{opacity:0}}
@keyframes evl-in-subir{from{opacity:0;transform:translateY(48px)}}
@keyframes evl-in-zoom{from{opacity:0;transform:scale(.9)}}
@keyframes evl-in-esquerda{from{opacity:0;transform:translateX(-60px)}}
@keyframes evl-in-direita{from{opacity:0;transform:translateX(60px)}}
@media (prefers-reduced-motion: no-preference){@supports (animation-timeline: view()){
.evl-a-fade .evl-w,.evl-a-subir .evl-w,.evl-a-zoom .evl-w,.evl-a-esquerda .evl-w,.evl-a-direita .evl-w{animation-timing-function:ease-out;animation-fill-mode:both;animation-timeline:view();animation-range:entry 0% entry 60%}
.evl-a-fade .evl-w{animation-name:evl-in-fade}.evl-a-subir .evl-w{animation-name:evl-in-subir}.evl-a-zoom .evl-w{animation-name:evl-in-zoom}
.evl-a-esquerda .evl-w{animation-name:evl-in-esquerda}.evl-a-direita .evl-w{animation-name:evl-in-direita}
}}
""";
}
