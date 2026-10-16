# Mapa de medidas DAX — BI CHS001

> Fórmulas extraídas do DataModel do PBIX original. Não são aproximações do frontend.

Total referenciado pelas dez páginas: 77 medidas.

## KPI — Activity Title

Uso: Resumo 30% / `9f5e7fa97182efd05fb0`; Resumo 60% / `ae8c65ffaa04a588c5a4`; Resumo 90% / `b3c33a1f9c33a350ed4e`.

```DAX
VAR DataSelecionada =
    SELECTEDVALUE('dSemana'[Data])

VAR InicioSemana =
    DataSelecionada - 6

VAR FimSemana =
    DataSelecionada

RETURN
IF(
    ISBLANK(DataSelecionada),
    "Weekly Progress",
    "Week - "
        & FORMAT(InicioSemana, "dd/MM")
        & " to "
        & FORMAT(FimSemana, "dd/MM")
)
```

## KPI — KPI 30 | 3D | COLUNA | % Realizado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`.

```DAX
VAR DataEixo =
    SELECTEDVALUE('dCalendario'[Data Corte])

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR InicioSemana =
    DataEixo - 6

VAR Documentos3D =
    CALCULATETABLE(
        VALUES('LDP (4)'[N&A DOCUMENT NUMBER]),
        KEEPFILTERS(
            'LDP (4)'[FORMAT] = "3D"
        )
    )

VAR TrabalhoLinhaBase3D30 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        TREATAS(
            Documentos3D,
            'WBX'[NA DOCUMENT NUMBER]
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )

VAR TrabalhoRealSemanaNormal3D30 =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        TREATAS(
            Documentos3D,
            'WBX'[NA DOCUMENT NUMBER]
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] >= InicioSemana
                && 'AssignmentByDay'[TimeByDay] < INT(DataEixo) + 1
        )
    )

VAR TrabalhoRealAntesSemana3D30 =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        TREATAS(
            Documentos3D,
            'WBX'[NA DOCUMENT NUMBER]
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] < InicioSemana
        )
    )

VAR TrabalhoRealTotal3D30 =
    CALCULATE(
        SUM('WBX'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        TREATAS(
            Documentos3D,
            'WBX'[NA DOCUMENT NUMBER]
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )

VAR SemanaDoCorte =
    DataEixo = DataCorte

VAR TrabalhoRealSemanaCorte3D30 =
    MAX(
        0,
        TrabalhoRealTotal3D30
            - COALESCE(
                TrabalhoRealAntesSemana3D30,
                0
            )
    )

VAR TrabalhoRealSemana3D30 =
    IF(
        SemanaDoCorte,
        TrabalhoRealSemanaCorte3D30,
        TrabalhoRealSemanaNormal3D30
    )

RETURN
IF(
    ISBLANK(DataEixo)
        || ISBLANK(DataCorte)
        || DataEixo > DataCorte
        || ISBLANK(TrabalhoLinhaBase3D30)
        || TrabalhoLinhaBase3D30 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoRealSemana3D30,
        TrabalhoLinhaBase3D30,
        0
    )
)
```

## KPI — KPI 30 | 3D | CURVA | % Realizado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`.

```DAX
VAR DataEixo =
    SELECTEDVALUE('dCalendario'[Data Corte])

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR Documentos3D =
    CALCULATETABLE(
        VALUES('LDP (4)'[N&A DOCUMENT NUMBER]),
        KEEPFILTERS(
            'LDP (4)'[FORMAT] = "3D"
        )
    )

VAR TrabalhoLinhaBase3D30 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        TREATAS(
            Documentos3D,
            'WBX'[NA DOCUMENT NUMBER]
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )

VAR TrabalhoRealHistorico3D30 =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        TREATAS(
            Documentos3D,
            'WBX'[NA DOCUMENT NUMBER]
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] < INT(DataEixo) + 1
        )
    )

VAR TrabalhoRealTotal3D30 =
    CALCULATE(
        SUM('WBX'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        TREATAS(
            Documentos3D,
            'WBX'[NA DOCUMENT NUMBER]
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )

VAR TrabalhoRealAcumulado3D30 =
    IF(
        DataEixo = DataCorte,
        TrabalhoRealTotal3D30,
        TrabalhoRealHistorico3D30
    )

RETURN
IF(
    ISBLANK(DataEixo)
        || ISBLANK(DataCorte)
        || DataEixo > DataCorte
        || ISBLANK(TrabalhoLinhaBase3D30)
        || TrabalhoLinhaBase3D30 = 0,
    BLANK(),
    MIN(
        1,
        DIVIDE(
            TrabalhoRealAcumulado3D30,
            TrabalhoLinhaBase3D30,
            0
        )
    )
)
```

## KPI — KPI 30 | ACC | % Desvio

Uso: Resumo 30% / `f7a2b922e8e7ec35227f`.

```DAX
VAR Planejado =
    [KPI 30 | ACC | % Planejado]

VAR Realizado =
    [KPI 30 | ACC | % Realizado]

RETURN
IF(
    ISBLANK(Planejado)
        || ISBLANK(Realizado),
    BLANK(),
    Realizado - Planejado
)
```

## KPI — KPI 30 | ACC | % Planejado

Uso: Resumo 30% / `322c94e19d367caa19e2`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR InicioSemana =
    CALCULATE(
        MAX('dCalendario'[Início da Semana]),
        FILTER(
            ALL('dCalendario'),
            'dCalendario'[Data] = DataCorte
        )
    )

VAR DataAvaliacao =
    InicioSemana + 7

VAR TrabalhoTotal30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        'WBX'[Trabalho]
    )

VAR TrabalhoAcumulado30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                DataAvaliacao < Inicio, 0,
                DataAvaliacao >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        DataAvaliacao,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

RETURN
IF(
    ISBLANK(DataCorte)
        || TrabalhoTotal30 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoAcumulado30,
        TrabalhoTotal30,
        0
    )
)
```

## KPI — KPI 30 | ACC | % Realizado

Uso: Resumo 30% / `959cb0df0729d907e717`.

```DAX
VAR TrabalhoReal30 =
    [KPI 30 | BASE | Trabalho Real]

VAR TrabalhoLinhaBase30 =
    [KPI 30 | BASE | Trabalho Linha Base]

RETURN
IF(
    ISBLANK(TrabalhoLinhaBase30)
        || TrabalhoLinhaBase30 = 0,
    BLANK(),
    MIN(
        1,
        DIVIDE(
            TrabalhoReal30,
            TrabalhoLinhaBase30,
            0
        )
    )
)
```

## KPI — KPI 30 | BASE SEM | % Desvio

Uso: Resumo 30% / `8c2e6dda007e8c3354c5`.

```DAX
VAR Planejado =
    [KPI 30 | BASE SEM | % Planejado]

VAR Realizado =
    [KPI 30 | BASE SEM | % Realizado]

RETURN
IF(
    ISBLANK(Planejado)
        || ISBLANK(Realizado),
    BLANK(),
    Realizado - Planejado
)
```

## KPI — KPI 30 | BASE SEM | % Planejado

Uso: Resumo 30% / `2fe2ff8d9e07d896037a`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR InicioSemana =
    CALCULATE(
        MAX('dCalendario'[Início da Semana]),
        FILTER(
            ALL('dCalendario'),
            'dCalendario'[Data] = DataCorte
        )
    )

VAR FimSemana =
    InicioSemana + 7

VAR TrabalhoTotal30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        'WBX'[Trabalho]
    )

VAR PlanejadoAteFimSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                FimSemana < Inicio, 0,
                FimSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        FimSemana,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR PlanejadoAntesSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                InicioSemana < Inicio, 0,
                InicioSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        InicioSemana,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR TrabalhoPlanejadoSemana =
    PlanejadoAteFimSemana - PlanejadoAntesSemana

RETURN
IF(
    ISBLANK(DataCorte)
        || TrabalhoTotal30 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoPlanejadoSemana,
        TrabalhoTotal30,
        0
    )
)
```

## KPI — KPI 30 | BASE SEM | % Realizado

Uso: Resumo 30% / `c4668e5b90042f7c1182`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR InicioSemana =
    CALCULATE(
        MAX('dCalendario'[Início da Semana]),
        FILTER(
            ALL('dCalendario'),
            'dCalendario'[Data] = DataCorte
        )
    )

VAR TrabalhoLinhaBase30 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        REMOVEFILTERS('dCalendario')
    )

VAR RealAteCorte =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] <= DataCorte
        )
    )

VAR RealAntesSemana =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] < InicioSemana
        )
    )

VAR TrabalhoRealSemana =
    RealAteCorte - RealAntesSemana

RETURN
IF(
    ISBLANK(DataCorte)
        || TrabalhoLinhaBase30 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoRealSemana,
        TrabalhoLinhaBase30,
        0
    )
)
```

## KPI — KPI 30 | COLUNA | % Baseline

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR InicioSemana =
    MAX('dCalendario'[Início da Semana])

VAR FimSemana =
    InicioSemana + 7

VAR TrabalhoTotal30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        'WBX'[Trabalho]
    )

VAR PlanejadoAteFimSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                FimSemana < Inicio, 0,
                FimSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        FimSemana,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR PlanejadoAntesSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                InicioSemana < Inicio, 0,
                InicioSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        InicioSemana,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR TrabalhoPlanejadoSemana =
    PlanejadoAteFimSemana -
    PlanejadoAntesSemana

RETURN
IF(
    TrabalhoTotal30 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoPlanejadoSemana,
        TrabalhoTotal30,
        0
    )
)
```

## KPI — KPI 30 | COLUNA | % Realizado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR InicioSemana =
    MAX('dCalendario'[Início da Semana])

VAR FimSemana =
    InicioSemana + 6

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TrabalhoLinhaBase30 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        REMOVEFILTERS('dCalendario')
    )

VAR LimiteReal =
    MIN(
        FimSemana,
        DataCorte
    )

VAR TrabalhoRealSemana =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] >= InicioSemana
                && 'AssignmentByDay'[TimeByDay] <= LimiteReal
        )
    )

RETURN
IF(
    ISBLANK(DataCorte)
        || InicioSemana > DataCorte
        || TrabalhoLinhaBase30 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoRealSemana,
        TrabalhoLinhaBase30,
        0
    )
)
```

## KPI — KPI 30 | CURVA | % Baseline

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 7

VAR DataMax =
    MAXX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        'WBX'[Término da Linha de Base]
    )

VAR DataAtual =
    MIN(
        DataEixo,
        DataMax
    )

VAR TrabalhoTotal30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        'WBX'[Trabalho]
    )

VAR TrabalhoAcumulado30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),

        VAR Inicio =
            'WBX'[Início da Linha de Base]

        VAR Termino =
            'WBX'[Término da Linha de Base]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                DataAtual < Inicio, 0,
                DataAtual >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        DataAtual,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR Resultado =
    DIVIDE(
        TrabalhoAcumulado30,
        TrabalhoTotal30,
        0
    )

RETURN
IF(
    ISBLANK(DataMax),
    BLANK(),
    IF(
        DataEixo >= DataMax,
        1,
        Resultado
    )
)
```

## KPI — KPI 30 | CURVA | % Realizado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 6

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TrabalhoLinhaBase30 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        REMOVEFILTERS('dCalendario')
    )

VAR DataLimite =
    MIN(
        DataEixo,
        DataCorte
    )

VAR TrabalhoRealAcumulado30 =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] <= DataLimite
        )
    )

VAR RealizadoHistorico =
    DIVIDE(
        TrabalhoRealAcumulado30,
        TrabalhoLinhaBase30,
        0
    )

VAR SemanaDoCorte =
    DataEixo >= DataCorte
        && DataEixo - 6 <= DataCorte

RETURN
SWITCH(
    TRUE(),

    ISBLANK(DataCorte)
        || TrabalhoLinhaBase30 = 0,
        BLANK(),

    DataEixo < DataCorte,
        MIN(
            1,
            RealizadoHistorico
        ),

    SemanaDoCorte,
        [KPI 30 | ACC | % Realizado],

    BLANK()
)
```

## KPI — KPI 30 | CURVA | % Reprogramado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 7

VAR DataMax =
    MAXX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        'WBX'[Término]
    )

VAR DataAtual =
    MIN(
        DataEixo,
        DataMax
    )

VAR TrabalhoTotal30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),
        'WBX'[Trabalho]
    )

VAR TrabalhoAcumulado30 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                DataAtual < Inicio, 0,
                DataAtual >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        DataAtual,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR Resultado =
    DIVIDE(
        TrabalhoAcumulado30,
        TrabalhoTotal30,
        0
    )

RETURN
IF(
    ISBLANK(DataMax),
    BLANK(),
    IF(
        DataEixo >= DataMax,
        1,
        Resultado
    )
)
```

## KPI — KPI 30 | CURVA | Mostrar Período

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; Resumo 30% / `ea13570fc26d9bbfeb65`.

```DAX
VAR DataSemana =
    MAX('dCalendario'[Início da Semana])

VAR InicioBaseline =
    CALCULATE(
        MIN('WBX'[Início da Linha de Base]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        )
    )

VAR InicioReprogramado =
    CALCULATE(
        MIN('WBX'[Início]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        )
    )

VAR TerminoBaseline =
    CALCULATE(
        MAX('WBX'[Término da Linha de Base]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        )
    )

VAR TerminoReprogramado =
    CALCULATE(
        MAX('WBX'[Término]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 30%"
        )
    )

VAR DataMin =
    MIN(
        InicioBaseline,
        InicioReprogramado
    )

VAR DataMax =
    MAX(
        TerminoBaseline,
        TerminoReprogramado
    )

RETURN
IF(
    DataSemana + 6 >= DataMin
        && DataSemana <= DataMax,
    1,
    0
)
```

## KPI — KPI 30 | PRAZO | Desvio

Uso: Resumo 30% / `8d19da71dfee6625ce73`.

```DAX
VAR TerminoBaseline =
    [KPI 30 | PRAZO | Término Baseline]

VAR TerminoReprogramado =
    [KPI 30 | PRAZO | Término Reprogramado]

RETURN
IF(
    ISBLANK(TerminoBaseline)
        || ISBLANK(TerminoReprogramado),
    BLANK(),
    DATEDIFF(
        TerminoBaseline,
        TerminoReprogramado,
        DAY
    )
)
```

## KPI — KPI 30 | PRAZO | Dias Restantes

Uso: Resumo 30% / `bb4e196646286dc3c5a8`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TerminoReprogramado =
    [KPI 30 | PRAZO | Término Reprogramado]

RETURN
IF(
    ISBLANK(DataCorte)
        || ISBLANK(TerminoReprogramado),
    BLANK(),
    MAX(
        0,
        DATEDIFF(
            DataCorte,
            TerminoReprogramado,
            DAY
        )
    )
)
```

## KPI — KPI 30 | PRAZO | Duração Baseline

Uso: Resumo 30% / `bb4e196646286dc3c5a8`.

```DAX
VAR InicioBaseline =
    [KPI 30 | PRAZO | Início Baseline]

VAR TerminoBaseline =
    [KPI 30 | PRAZO | Término Baseline]

RETURN
IF(
    ISBLANK(InicioBaseline)
        || ISBLANK(TerminoBaseline),
    BLANK(),
    DATEDIFF(
        InicioBaseline,
        TerminoBaseline,
        DAY
    )
)
```

## KPI — KPI 30 | PRAZO | Início Baseline

Uso: Resumo 30% / `48adb2e2db6a126e6823`.

```DAX
CALCULATE(
    MIN('WBX'[Início da Linha de Base]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 30%"
    )
)
```

## KPI — KPI 30 | PRAZO | Início Reprogramado

Uso: Resumo 30% / `48adb2e2db6a126e6823`.

```DAX
CALCULATE(
    MIN('WBX'[Início]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 30%"
    )
)
```

## KPI — KPI 30 | PRAZO | Término Baseline

Uso: Resumo 30% / `48adb2e2db6a126e6823`; Resumo 30% / `6d9c8b4630c4c1f0282e`.

```DAX
CALCULATE(
    MAX('WBX'[Término da Linha de Base]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 30%"
    )
)
```

## KPI — KPI 30 | PRAZO | Término Reprogramado

Uso: Resumo 30% / `48adb2e2db6a126e6823`; Resumo 30% / `6d9c8b4630c4c1f0282e`.

```DAX
CALCULATE(
    MAX('WBX'[Término]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 30%"
    )
)
```

## KPI — KPI 60 | ACC | % Desvio

Uso: Resumo 60% / `c88d9bd5241e0b16e414`.

```DAX
VAR Planejado =
    [KPI 60 | ACC | % Planejado]

VAR Realizado =
    [KPI 60 | ACC | % Realizado]

RETURN
IF(
    ISBLANK(Planejado)
        || ISBLANK(Realizado),
    BLANK(),
    Realizado - Planejado
)
```

## KPI — KPI 60 | ACC | % Planejado

Uso: Resumo 60% / `68d2224dc725813a357f`.

```DAX
VAR Corte = [KPI | CORTE | Data Corte]
RETURN IF(ISBLANK(Corte), BLANK(), CALCULATE([KPI 60 | CURVA | % Reprogramado],
        REMOVEFILTERS('dCalendario'), REMOVEFILTERS('dSemana'),
        TREATAS({ Corte }, 'dCalendario'[Data Corte])))
```

## KPI — KPI 60 | ACC | % Realizado

Uso: Resumo 60% / `288c0b007a0ead1549b2`.

```DAX
VAR TrabalhoReal60 =
    [KPI 60 | BASE | Trabalho Real]

VAR TrabalhoLinhaBase60 =
    [KPI 60 | BASE | Trabalho Linha Base]

RETURN
IF(
    ISBLANK(TrabalhoLinhaBase60)
        || TrabalhoLinhaBase60 = 0,
    BLANK(),
    MIN(
        1,
        DIVIDE(
            TrabalhoReal60,
            TrabalhoLinhaBase60,
            0
        )
    )
)
```

## KPI — KPI 60 | BASE SEM | % Desvio

Uso: Resumo 60% / `c67b041d0188f0b43075`.

```DAX
VAR Planejado =
    [KPI 60 | BASE SEM | % Planejado]

VAR Realizado =
    [KPI 60 | BASE SEM | % Realizado]

RETURN
IF(
    ISBLANK(Planejado)
        || ISBLANK(Realizado),
    BLANK(),
    Realizado - Planejado
)
```

## KPI — KPI 60 | BASE SEM | % Planejado

Uso: Resumo 60% / `df5ca39b703d47ce86e5`.

```DAX
VAR Corte = [KPI | CORTE | Data Corte]
RETURN IF(ISBLANK(Corte), BLANK(), CALCULATE([KPI 60 | COLUNA | % Reprogramado],
        REMOVEFILTERS('dCalendario'), REMOVEFILTERS('dSemana'),
        TREATAS({ Corte }, 'dCalendario'[Data Corte])))
```

## KPI — KPI 60 | BASE SEM | % Realizado

Uso: Resumo 60% / `81e54c4b6acead0c8c1e`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR InicioSemana =
    CALCULATE(
        MAX('dCalendario'[Início da Semana]),
        FILTER(
            ALL('dCalendario'),
            'dCalendario'[Data] = DataCorte
        )
    )

VAR TrabalhoLinhaBase60 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        REMOVEFILTERS('dCalendario')
    )

VAR RealAteCorte =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] < INT(DataCorte) + 1
        )
    )

VAR RealAntesSemana =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] < InicioSemana
        )
    )

VAR TrabalhoRealSemana =
    RealAteCorte -
    RealAntesSemana

RETURN
IF(
    ISBLANK(DataCorte)
        || TrabalhoLinhaBase60 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoRealSemana,
        TrabalhoLinhaBase60,
        0
    )
)
```

## KPI — KPI 60 | COLUNA | % Baseline

Uso: LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR Corte = SELECTEDVALUE('dCalendario'[Data Corte])
VAR Anterior = Corte - 7
VAR Atual = CALCULATE([KPI 60 | CURVA | % Baseline],
        REMOVEFILTERS('dCalendario'), REMOVEFILTERS('dSemana'),
        TREATAS({ Corte }, 'dCalendario'[Data Corte]))
VAR Antes = CALCULATE([KPI 60 | CURVA | % Baseline],
        REMOVEFILTERS('dCalendario'), REMOVEFILTERS('dSemana'),
        TREATAS({ Anterior }, 'dCalendario'[Data Corte]))
RETURN IF(ISBLANK(Corte) || ISBLANK(Atual) || ISBLANK(Antes), BLANK(), Atual - Antes)
```

## KPI — KPI 60 | COLUNA | % Realizado

Uso: LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR InicioSemana =
    MAX('dCalendario'[Início da Semana])

VAR FimSemana =
    InicioSemana + 6

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TrabalhoLinhaBase60 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        REMOVEFILTERS('dCalendario')
    )

VAR LimiteReal =
    MIN(
        FimSemana,
        DataCorte
    )

VAR TrabalhoRealSemana =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] >= InicioSemana
                && 'AssignmentByDay'[TimeByDay] < INT(LimiteReal) + 1
        )
    )

RETURN
IF(
    ISBLANK(DataCorte)
        || InicioSemana > DataCorte
        || TrabalhoLinhaBase60 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoRealSemana,
        TrabalhoLinhaBase60,
        0
    )
)
```

## KPI — KPI 60 | CURVA | % Baseline

Uso: LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo = SELECTEDVALUE('dCalendario'[Data Corte])
VAR Corte = INT(DataEixo) + TIME(17, 0, 0)
VAR Atividades =
    CALCULATETABLE(
        FILTER('WBX',
            'WBX'[Level 4] = "Engineering - 60%"
                && 'WBX'[É Resumo] = FALSE()
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )
VAR Invalidas = COUNTROWS(FILTER(Atividades,
    'WBX'[Trabalho] < 0 ||
    ('WBX'[Trabalho] > 0 &&
        (ISBLANK('WBX'[Início da Linha de Base]) || ISBLANK('WBX'[Término da Linha de Base])
            || 'WBX'[Término da Linha de Base] < 'WBX'[Início da Linha de Base])))
)
VAR Total = SUMX(Atividades, 'WBX'[Trabalho])
VAR Previsto = SUMX(Atividades,
    VAR Inicio = 'WBX'[Início da Linha de Base]
    VAR Fim = 'WBX'[Término da Linha de Base]
    VAR Peso = 'WBX'[Trabalho]
    RETURN IF(Peso > 0 && NOT ISBLANK(Inicio) && NOT ISBLANK(Fim) && Fim >= Inicio,
        VAR Fracao = SWITCH(TRUE(),
            Corte >= Fim, 1,
            Corte <= Inicio, 0,
            VAR Dias = FILTER(CALENDAR(INT(Inicio), INT(Fim)),
                WEEKDAY([Date], 2) <= 5 && [Date] <> DATE(2026, 5, 1))
            VAR Duracao = SUMX(Dias,
                VAR D = [Date]
                RETURN 1440 * (
                    MAX(0, MIN(Fim, D + TIME(12,0,0)) - MAX(Inicio, D + TIME(8,0,0))) +
                    MAX(0, MIN(Fim, D + TIME(17,0,0)) - MAX(Inicio, D + TIME(13,0,0)))
                ))
            VAR Decorrido = SUMX(Dias,
                VAR D = [Date]
                RETURN 1440 * (
                    MAX(0, MIN(Corte, D + TIME(12,0,0)) - MAX(Inicio, D + TIME(8,0,0))) +
                    MAX(0, MIN(Corte, D + TIME(17,0,0)) - MAX(Inicio, D + TIME(13,0,0)))
                ))
            RETURN DIVIDE(Decorrido, Duracao)
        )
        RETURN Peso * Fracao,
        0
    )
)
RETURN IF(ISBLANK(DataEixo) || COUNTROWS(Atividades) = 0 || Invalidas > 0,
    BLANK(), DIVIDE(Previsto, Total, 1))
```

## KPI — KPI 60 | CURVA | % Realizado

Uso: LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    SELECTEDVALUE('dCalendario'[Data Corte])

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TrabalhoLinhaBase60 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )

VAR TrabalhoRealHistorico60 =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] < INT(DataEixo) + 1
        )
    )

VAR TrabalhoRealTotal60 =
    CALCULATE(
        SUM('WBX'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 60%"
        ),
        KEEPFILTERS(
            'WBX'[É Resumo] = FALSE()
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )

VAR TrabalhoRealAcumulado60 =
    IF(
        DataEixo = DataCorte,
        TrabalhoRealTotal60,
        TrabalhoRealHistorico60
    )

RETURN
IF(
    ISBLANK(DataEixo)
        || ISBLANK(DataCorte)
        || DataEixo > DataCorte
        || ISBLANK(TrabalhoLinhaBase60)
        || TrabalhoLinhaBase60 = 0,
    BLANK(),
    MIN(
        1,
        DIVIDE(
            TrabalhoRealAcumulado60,
            TrabalhoLinhaBase60,
            0
        )
    )
)
```

## KPI — KPI 60 | CURVA | % Reprogramado

Uso: LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo = SELECTEDVALUE('dCalendario'[Data Corte])
VAR Corte = INT(DataEixo) + TIME(17, 0, 0)
VAR Atividades =
    CALCULATETABLE(
        FILTER('WBX',
            'WBX'[Level 4] = "Engineering - 60%"
                && 'WBX'[É Resumo] = FALSE()
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )
VAR Invalidas = COUNTROWS(FILTER(Atividades,
    'WBX'[Trabalho] < 0 ||
    ('WBX'[Trabalho] > 0 &&
        (ISBLANK('WBX'[Início]) || ISBLANK('WBX'[Término])
            || 'WBX'[Término] < 'WBX'[Início])))
)
VAR Total = SUMX(Atividades, 'WBX'[Trabalho])
VAR Previsto = SUMX(Atividades,
    VAR Inicio = 'WBX'[Início]
    VAR Fim = 'WBX'[Término]
    VAR Peso = 'WBX'[Trabalho]
    RETURN IF(Peso > 0 && NOT ISBLANK(Inicio) && NOT ISBLANK(Fim) && Fim >= Inicio,
        VAR Fracao = SWITCH(TRUE(),
            Corte >= Fim, 1,
            Corte <= Inicio, 0,
            VAR Dias = FILTER(CALENDAR(INT(Inicio), INT(Fim)),
                WEEKDAY([Date], 2) <= 5 && [Date] <> DATE(2026, 5, 1))
            VAR Duracao = SUMX(Dias,
                VAR D = [Date]
                RETURN 1440 * (
                    MAX(0, MIN(Fim, D + TIME(12,0,0)) - MAX(Inicio, D + TIME(8,0,0))) +
                    MAX(0, MIN(Fim, D + TIME(17,0,0)) - MAX(Inicio, D + TIME(13,0,0)))
                ))
            VAR Decorrido = SUMX(Dias,
                VAR D = [Date]
                RETURN 1440 * (
                    MAX(0, MIN(Corte, D + TIME(12,0,0)) - MAX(Inicio, D + TIME(8,0,0))) +
                    MAX(0, MIN(Corte, D + TIME(17,0,0)) - MAX(Inicio, D + TIME(13,0,0)))
                ))
            RETURN DIVIDE(Decorrido, Duracao)
        )
        RETURN Peso * Fracao,
        0
    )
)
RETURN IF(ISBLANK(DataEixo) || COUNTROWS(Atividades) = 0 || Invalidas > 0,
    BLANK(), DIVIDE(Previsto, Total, 1))
```

## KPI — KPI 60 | CURVA | Mostrar Período

Uso: LDP - 3D - 60% / `9bd852d7dad89a36ea49`; Resumo 60% / `2dbe2e245f07d1e69f81`.

```DAX
VAR DataSemana =
    MAX('dCalendario'[Início da Semana])

VAR InicioBaseline =
    CALCULATE(
        MIN('WBX'[Início da Linha de Base]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 60%"
        )
    )

VAR InicioReprogramado =
    CALCULATE(
        MIN('WBX'[Início]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 60%"
        )
    )

VAR TerminoBaseline =
    CALCULATE(
        MAX('WBX'[Término da Linha de Base]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 60%"
        )
    )

VAR TerminoReprogramado =
    CALCULATE(
        MAX('WBX'[Término]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 60%"
        )
    )

VAR DataMin =
    MIN(InicioBaseline, InicioReprogramado)

VAR DataMax =
    MAX(TerminoBaseline, TerminoReprogramado)

RETURN
IF(
    DataSemana + 6 >= DataMin
        && DataSemana <= DataMax,
    1,
    0
)
```

## KPI — KPI 60 | PRAZO | Desvio

Uso: Resumo 60% / `acd6bef2d1efb2e24ac4`.

```DAX
VAR TerminoBaseline =
    [KPI 60 | PRAZO | Término Baseline]

VAR TerminoReprogramado =
    [KPI 60 | PRAZO | Término Reprogramado]

RETURN
IF(
    ISBLANK(TerminoBaseline)
        || ISBLANK(TerminoReprogramado),
    BLANK(),
    DATEDIFF(
        TerminoBaseline,
        TerminoReprogramado,
        DAY
    )
)
```

## KPI — KPI 60 | PRAZO | Dias Restantes

Uso: Resumo 60% / `c859d8dfdb220c4f7c8e`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TerminoReprogramado =
    [KPI 60 | PRAZO | Término Reprogramado]

RETURN
IF(
    ISBLANK(DataCorte)
        || ISBLANK(TerminoReprogramado),
    BLANK(),
    MAX(
        0,
        DATEDIFF(
            DataCorte,
            TerminoReprogramado,
            DAY
        )
    )
)
```

## KPI — KPI 60 | PRAZO | Duração Baseline

Uso: Resumo 60% / `c859d8dfdb220c4f7c8e`.

```DAX
VAR InicioBaseline =
    [KPI 60 | PRAZO | Início Baseline]

VAR TerminoBaseline =
    [KPI 60 | PRAZO | Término Baseline]

RETURN
IF(
    ISBLANK(InicioBaseline)
        || ISBLANK(TerminoBaseline),
    BLANK(),
    DATEDIFF(
        InicioBaseline,
        TerminoBaseline,
        DAY
    )
)
```

## KPI — KPI 60 | PRAZO | Início Baseline

Uso: Resumo 60% / `8e01a472a4d77433153d`.

```DAX
CALCULATE(
    MIN('WBX'[Início da Linha de Base]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 60%"
    )
)
```

## KPI — KPI 60 | PRAZO | Início Reprogramado

Uso: Resumo 60% / `8e01a472a4d77433153d`.

```DAX
CALCULATE(
    MIN('WBX'[Início]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 60%"
    )
)
```

## KPI — KPI 60 | PRAZO | Término Baseline

Uso: Resumo 60% / `1cb81bc001906b9e973d`; Resumo 60% / `8e01a472a4d77433153d`.

```DAX
CALCULATE(
    MAX('WBX'[Término da Linha de Base]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 60%"
    )
)
```

## KPI — KPI 60 | PRAZO | Término Reprogramado

Uso: Resumo 60% / `1cb81bc001906b9e973d`; Resumo 60% / `8e01a472a4d77433153d`.

```DAX
CALCULATE(
    MAX('WBX'[Término]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 60%"
    )
)
```

## KPI — KPI 90 | ACC | % Desvio

Uso: Resumo 90% / `ad4dde21f1811240207a`.

```DAX
VAR Planejado =
    [KPI 90 | ACC | % Planejado]

VAR Realizado =
    [KPI 90 | ACC | % Realizado]

RETURN
IF(
    ISBLANK(Planejado)
        || ISBLANK(Realizado),
    BLANK(),
    Realizado - Planejado
)
```

## KPI — KPI 90 | ACC | % Planejado

Uso: Resumo 90% / `703423ed3cf746656234`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR InicioSemana =
    CALCULATE(
        MAX('dCalendario'[Início da Semana]),
        FILTER(
            ALL('dCalendario'),
            'dCalendario'[Data] = DataCorte
        )
    )

VAR DataAvaliacao =
    InicioSemana + 7

VAR TrabalhoTotal90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        'WBX'[Trabalho]
    )

VAR TrabalhoAcumulado90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),

        VAR Inicio = 'WBX'[Início]
        VAR Termino = 'WBX'[Término]
        VAR Peso = 'WBX'[Trabalho]
        VAR Duracao = DATEDIFF(Inicio, Termino, DAY)

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                DataAvaliacao < Inicio, 0,
                DataAvaliacao >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(Inicio, DataAvaliacao, DAY),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

RETURN
IF(
    ISBLANK(DataCorte)
        || TrabalhoTotal90 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoAcumulado90,
        TrabalhoTotal90,
        0
    )
)
```

## KPI — KPI 90 | ACC | % Realizado

Uso: Resumo 90% / `a5a78671a7e188397cd0`.

```DAX
VAR TrabalhoReal90 =
    [KPI 90 | BASE | Trabalho Real]

VAR TrabalhoLinhaBase90 =
    [KPI 90 | BASE | Trabalho Linha Base]

RETURN
IF(
    ISBLANK(TrabalhoLinhaBase90)
        || TrabalhoLinhaBase90 = 0,
    BLANK(),
    MIN(
        1,
        DIVIDE(
            TrabalhoReal90,
            TrabalhoLinhaBase90,
            0
        )
    )
)
```

## KPI — KPI 90 | BASE SEM | % Planejado

Uso: Resumo 90% / `4d42161abc0acb822bbb`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR InicioSemana =
    CALCULATE(
        MAX('dCalendario'[Início da Semana]),
        FILTER(
            ALL('dCalendario'),
            'dCalendario'[Data] = DataCorte
        )
    )

VAR FimSemana =
    InicioSemana + 7

VAR TrabalhoTotal90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        'WBX'[Trabalho]
    )

VAR PlanejadoAteFimSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),

        VAR Inicio = 'WBX'[Início]
        VAR Termino = 'WBX'[Término]
        VAR Peso = 'WBX'[Trabalho]
        VAR Duracao = DATEDIFF(Inicio, Termino, DAY)

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                FimSemana < Inicio, 0,
                FimSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(Inicio, FimSemana, DAY),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR PlanejadoAntesSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),

        VAR Inicio = 'WBX'[Início]
        VAR Termino = 'WBX'[Término]
        VAR Peso = 'WBX'[Trabalho]
        VAR Duracao = DATEDIFF(Inicio, Termino, DAY)

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                InicioSemana < Inicio, 0,
                InicioSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(Inicio, InicioSemana, DAY),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR TrabalhoPlanejadoSemana =
    PlanejadoAteFimSemana -
    PlanejadoAntesSemana

RETURN
IF(
    ISBLANK(DataCorte)
        || TrabalhoTotal90 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoPlanejadoSemana,
        TrabalhoTotal90,
        0
    )
)
```

## KPI — KPI 90 | CARD | Weekly Complete

Uso: Resumo 90% / `b88b6d735ef6f6bfb988`.

```DAX
VAR Valor =
    [KPI 90 | BASE SEM | % Realizado]

RETURN
IF(
    ISBLANK(Valor),
    "",
    FORMAT(
        Valor,
        "0.00%"
    )
)
```

## KPI — KPI 90 | CARD | Weekly Deviation

Uso: Resumo 90% / `f07c53307af0c5f22a9b`.

```DAX
VAR Valor =
    [KPI 90 | BASE SEM | % Desvio]

RETURN
IF(
    ISBLANK(Valor),
    "",
    FORMAT(
        Valor,
        "0.00%;-0.00%;0.00%"
    )
)
```

## KPI — KPI 90 | COLUNA | % Baseline

Uso: LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR InicioSemana =
    MAX('dCalendario'[Início da Semana])

VAR FimSemana =
    InicioSemana + 7

VAR TrabalhoTotal90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        'WBX'[Trabalho]
    )

VAR PlanejadoAteFimSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                FimSemana < Inicio, 0,
                FimSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        FimSemana,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR PlanejadoAntesSemana =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                InicioSemana < Inicio, 0,
                InicioSemana >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        InicioSemana,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR TrabalhoPlanejadoSemana =
    PlanejadoAteFimSemana -
    PlanejadoAntesSemana

RETURN
IF(
    TrabalhoTotal90 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoPlanejadoSemana,
        TrabalhoTotal90,
        0
    )
)
```

## KPI — KPI 90 | COLUNA | % Realizado

Uso: LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR InicioSemana =
    MAX('dCalendario'[Início da Semana])

VAR FimSemana =
    InicioSemana + 6

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TrabalhoLinhaBase90 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        REMOVEFILTERS('dCalendario')
    )

VAR LimiteReal =
    MIN(
        FimSemana,
        DataCorte
    )

VAR TrabalhoRealSemana =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] >= InicioSemana
                && 'AssignmentByDay'[TimeByDay] <= LimiteReal
        )
    )

RETURN
IF(
    ISBLANK(DataCorte)
        || InicioSemana > DataCorte
        || TrabalhoLinhaBase90 = 0,
    BLANK(),
    DIVIDE(
        TrabalhoRealSemana,
        TrabalhoLinhaBase90,
        0
    )
)
```

## KPI — KPI 90 | CURVA | % Baseline

Uso: LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 7

VAR DataMax =
    MAXX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        'WBX'[Término da Linha de Base]
    )

VAR DataAtual =
    MIN(DataEixo, DataMax)

VAR TrabalhoTotal90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        'WBX'[Trabalho]
    )

VAR TrabalhoAcumulado90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),

        VAR Inicio =
            'WBX'[Início da Linha de Base]

        VAR Termino =
            'WBX'[Término da Linha de Base]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(Inicio, Termino, DAY)

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                DataAtual < Inicio, 0,
                DataAtual >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(Inicio, DataAtual, DAY),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR Resultado =
    DIVIDE(
        TrabalhoAcumulado90,
        TrabalhoTotal90,
        0
    )

RETURN
IF(
    ISBLANK(DataMax),
    BLANK(),
    IF(
        DataEixo >= DataMax,
        1,
        Resultado
    )
)
```

## KPI — KPI 90 | CURVA | % Realizado

Uso: LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 6

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TrabalhoLinhaBase90 =
    CALCULATE(
        SUM('WBX'[Trab Linha de Base]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        REMOVEFILTERS('dCalendario')
    )

VAR DataLimite =
    MIN(
        DataEixo,
        DataCorte
    )

VAR TrabalhoRealAcumulado90 =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        KEEPFILTERS(
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] <= DataLimite
        )
    )

VAR RealizadoHistorico =
    DIVIDE(
        TrabalhoRealAcumulado90,
        TrabalhoLinhaBase90,
        0
    )

VAR SemanaDoCorte =
    DataEixo >= DataCorte
        && DataEixo - 6 <= DataCorte

RETURN
SWITCH(
    TRUE(),

    ISBLANK(DataCorte)
        || TrabalhoLinhaBase90 = 0,
        BLANK(),

    DataEixo < DataCorte,
        MIN(
            1,
            RealizadoHistorico
        ),

    SemanaDoCorte,
        [KPI 90 | ACC | % Realizado],

    BLANK()
)
```

## KPI — KPI 90 | CURVA | % Reprogramado

Uso: LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 7

VAR DataMax =
    MAXX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        'WBX'[Término]
    )

VAR DataAtual =
    MIN(DataEixo, DataMax)

VAR TrabalhoTotal90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),
        'WBX'[Trabalho]
    )

VAR TrabalhoAcumulado90 =
    SUMX(
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        ),

        VAR Inicio =
            'WBX'[Início]

        VAR Termino =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(Inicio, Termino, DAY)

        VAR Avanco =
            SWITCH(
                TRUE(),
                ISBLANK(Inicio) || ISBLANK(Termino), 0,
                DataAtual < Inicio, 0,
                DataAtual >= Termino, 1,
                Duracao <= 0, 1,
                DIVIDE(
                    DATEDIFF(Inicio, DataAtual, DAY),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR Resultado =
    DIVIDE(
        TrabalhoAcumulado90,
        TrabalhoTotal90,
        0
    )

RETURN
IF(
    ISBLANK(DataMax),
    BLANK(),
    IF(
        DataEixo >= DataMax,
        1,
        Resultado
    )
)
```

## KPI — KPI 90 | CURVA | Mostrar Período

Uso: LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataSemana =
    MAX('dCalendario'[Início da Semana])

VAR InicioBaseline =
    CALCULATE(
        MIN('WBX'[Início da Linha de Base]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        )
    )

VAR InicioReprogramado =
    CALCULATE(
        MIN('WBX'[Início]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        )
    )

VAR TerminoBaseline =
    CALCULATE(
        MAX('WBX'[Término da Linha de Base]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        )
    )

VAR TerminoReprogramado =
    CALCULATE(
        MAX('WBX'[Término]),
        FILTER(
            ALLSELECTED('WBX'),
            'WBX'[Level 4] = "Engineering - 90%"
        )
    )

VAR DataMin =
    MIN(InicioBaseline, InicioReprogramado)

VAR DataMax =
    MAX(TerminoBaseline, TerminoReprogramado)

RETURN
IF(
    DataSemana + 6 >= DataMin
        && DataSemana <= DataMax,
    1,
    0
)
```

## KPI — KPI 90 | PRAZO | Desvio

Uso: Resumo 90% / `9a23de1bd69e9fb00a02`.

```DAX
VAR TerminoBaseline =
    [KPI 90 | PRAZO | Término Baseline]

VAR TerminoReprogramado =
    [KPI 90 | PRAZO | Término Reprogramado]

RETURN
IF(
    ISBLANK(TerminoBaseline)
        || ISBLANK(TerminoReprogramado),
    BLANK(),
    DATEDIFF(
        TerminoBaseline,
        TerminoReprogramado,
        DAY
    )
)
```

## KPI — KPI 90 | PRAZO | Dias Restantes

Uso: Resumo 90% / `819db0fa8c16f76fba3a`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TerminoReprogramado =
    [KPI 90 | PRAZO | Término Reprogramado]

RETURN
IF(
    ISBLANK(DataCorte)
        || ISBLANK(TerminoReprogramado),
    BLANK(),
    MAX(
        0,
        DATEDIFF(
            DataCorte,
            TerminoReprogramado,
            DAY
        )
    )
)
```

## KPI — KPI 90 | PRAZO | Duração Baseline

Uso: Resumo 90% / `819db0fa8c16f76fba3a`.

```DAX
VAR InicioBaseline =
    [KPI 90 | PRAZO | Início Baseline]

VAR TerminoBaseline =
    [KPI 90 | PRAZO | Término Baseline]

RETURN
IF(
    ISBLANK(InicioBaseline)
        || ISBLANK(TerminoBaseline),
    BLANK(),
    DATEDIFF(
        InicioBaseline,
        TerminoBaseline,
        DAY
    )
)
```

## KPI — KPI 90 | PRAZO | Início Baseline

Uso: Resumo 90% / `485db311e7e38517d3a1`.

```DAX
CALCULATE(
    MIN('WBX'[Início da Linha de Base]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 90%"
    )
)
```

## KPI — KPI 90 | PRAZO | Início Reprogramado

Uso: Resumo 90% / `485db311e7e38517d3a1`.

```DAX
CALCULATE(
    MIN('WBX'[Início]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 90%"
    )
)
```

## KPI — KPI 90 | PRAZO | Término Baseline

Uso: Resumo 90% / `485db311e7e38517d3a1`; Resumo 90% / `bc7e47d19ecccb00d5c6`.

```DAX
CALCULATE(
    MAX('WBX'[Término da Linha de Base]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 90%"
    )
)
```

## KPI — KPI 90 | PRAZO | Término Reprogramado

Uso: Resumo 90% / `485db311e7e38517d3a1`; Resumo 90% / `bc7e47d19ecccb00d5c6`.

```DAX
CALCULATE(
    MAX('WBX'[Término]),
    FILTER(
        ALLSELECTED('WBX'),
        'WBX'[Level 4] = "Engineering - 90%"
    )
)
```

## KPI — KPI | COLUNA | % Baseline

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
DIVIDE(
    [KPI | BASE | Trabalho Linha Base],
    [KPI | BASE | HH Total Linha Base],
    0
)
```

## KPI — KPI | COLUNA | % Realizado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR DataSemana =
    MAX('dCalendario'[Data Corte])

VAR RealSemana =
    [KPI | BASE | Trabalho Real]

VAR RealFuturo =
    CALCULATE(
        [KPI | BASE | Trabalho Real],
        FILTER(
            ALL('dCalendario'[Data]),
            'dCalendario'[Data] > DataCorte
        )
    )

RETURN
SWITCH(
    TRUE(),

    ISBLANK(DataCorte),
        BLANK(),

    DataSemana > DataCorte,
        BLANK(),

    DataSemana = DataCorte,
        DIVIDE(
            RealSemana + RealFuturo,
            [KPI | BASE | HH Total Tendência],
            0
        ),

    DIVIDE(
        RealSemana,
        [KPI | BASE | HH Total Tendência],
        0
    )
)
```

## KPI — KPI | CURVA | % Baseline

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 7

VAR DataMax =
    MAXX(
        ALLSELECTED('WBX'),
        'WBX'[Término da Linha de Base]
    )

VAR DataAtual =
    MIN(
        DataEixo,
        DataMax
    )

VAR TrabalhoTotal =
    SUMX(
        ALLSELECTED('WBX'),
        'WBX'[Trabalho]
    )

VAR TrabalhoAcumulado =
    SUMX(
        ALLSELECTED('WBX'),

        VAR Inicio =
            'WBX'[Início da Linha de Base]

        VAR Termino =
            'WBX'[Término da Linha de Base]

        VAR Peso =
            'WBX'[Trabalho]

        VAR Duracao =
            DATEDIFF(
                Inicio,
                Termino,
                DAY
            )

        VAR Avanco =
            SWITCH(
                TRUE(),

                ISBLANK(Inicio) ||
                ISBLANK(Termino),
                    0,

                DataAtual < Inicio,
                    0,

                DataAtual >= Termino,
                    1,

                Duracao <= 0,
                    1,

                DIVIDE(
                    DATEDIFF(
                        Inicio,
                        DataAtual,
                        DAY
                    ),
                    Duracao,
                    0
                )
            )

        RETURN
            Peso * Avanco
    )

VAR Resultado =
    DIVIDE(
        TrabalhoAcumulado,
        TrabalhoTotal,
        0
    )

RETURN
IF(
    ISBLANK(DataMax),
    BLANK(),
    IF(
        DataEixo >= DataMax,
        1,
        Resultado
    )
)
```

## KPI — KPI | CURVA | % Realizado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    MAX('dCalendario'[Início da Semana]) + 6

VAR DataCorte =
    [KPI | CORTE | Data Corte]

VAR TrabalhoTotal =
    CALCULATE(
        SUM('WBX'[Trabalho]),
        REMOVEFILTERS('dCalendario')
    )

VAR TrabalhoRealAcumulado =
    CALCULATE(
        SUM('AssignmentByDay'[Trabalho Real]),
        REMOVEFILTERS('dCalendario'),
        FILTER(
            ALL('AssignmentByDay'[TimeByDay]),
            'AssignmentByDay'[TimeByDay] <= DataEixo
        )
    )

VAR RealizadoHistorico =
    DIVIDE(
        TrabalhoRealAcumulado,
        TrabalhoTotal,
        0
    )

VAR SemanaDoCorte =
    DataEixo >= DataCorte &&
    DataEixo - 6 <= DataCorte

RETURN
SWITCH(
    TRUE(),

    ISBLANK(DataCorte),
        BLANK(),

    DataEixo < DataCorte,
        RealizadoHistorico,

    SemanaDoCorte,
        [KPI | ACC | % Realizado],

    BLANK()
)
```

## KPI — KPI | CURVA | % Reprogramado

Uso: LDP - 3D - 30% / `59602fe37f9bfac4c173`; LDP - 3D - 60% / `9bd852d7dad89a36ea49`; LDP - 3D - 90% / `2c9e8683b347b30efac7`; Resumo 30% / `ea13570fc26d9bbfeb65`; Resumo 60% / `2dbe2e245f07d1e69f81`; Resumo 90% / `05c37b733f3141ff0651`.

```DAX
VAR DataEixo =
    SELECTEDVALUE('dCalendario'[Data Corte])

VAR Corte =
    INT(DataEixo) + TIME(17, 0, 0)

VAR Atividades =
    CALCULATETABLE(
        FILTER(
            'WBX',
            'WBX'[É Resumo] = FALSE()
        ),
        REMOVEFILTERS('dCalendario'),
        REMOVEFILTERS('dSemana')
    )

VAR Invalidas =
    COUNTROWS(
        FILTER(
            Atividades,
            'WBX'[Trabalho] < 0
                ||
            (
                'WBX'[Trabalho] > 0
                    &&
                (
                    ISBLANK('WBX'[Início])
                        ||
                    ISBLANK('WBX'[Término])
                        ||
                    'WBX'[Término] < 'WBX'[Início]
                )
            )
        )
    )

VAR Total =
    SUMX(
        Atividades,
        'WBX'[Trabalho]
    )

VAR Previsto =
    SUMX(
        Atividades,

        VAR Inicio =
            'WBX'[Início]

        VAR Fim =
            'WBX'[Término]

        VAR Peso =
            'WBX'[Trabalho]

        RETURN
            IF(
                Peso > 0
                    &&
                NOT ISBLANK(Inicio)
                    &&
                NOT ISBLANK(Fim)
                    &&
                Fim >= Inicio,

                VAR Fracao =
                    SWITCH(
                        TRUE(),

                        Corte >= Fim,
                            1,

                        Corte <= Inicio,
                            0,

                        VAR Dias =
                            FILTER(
                                CALENDAR(
                                    INT(Inicio),
                                    INT(Fim)
                                ),
                                WEEKDAY([Date], 2) <= 5
                                    &&
                                [Date] <> DATE(2026, 5, 1)
                            )

                        VAR Duracao =
                            SUMX(
                                Dias,

                                VAR D =
                                    [Date]

                                RETURN
                                    1440
                                        *
                                    (
                                        MAX(
                                            0,
                                            MIN(
                                                Fim,
                                                D + TIME(12, 0, 0)
                                            )
                                                -
                                            MAX(
                                                Inicio,
                                                D + TIME(8, 0, 0)
                                            )
                                        )
                                            +
                                        MAX(
                                            0,
                                            MIN(
                                                Fim,
                                                D + TIME(17, 0, 0)
                                            )
                                                -
                                            MAX(
                                                Inicio,
                                                D + TIME(13, 0, 0)
                                            )
                                        )
                                    )
                            )

                        VAR Decorrido =
                            SUMX(
                                Dias,

                                VAR D =
                                    [Date]

                                RETURN
                                    1440
                                        *
                                    (
                                        MAX(
                                            0,
                                            MIN(
                                                Corte,
                                                D + TIME(12, 0, 0)
                                            )
                                                -
                                            MAX(
                                                Inicio,
                                                D + TIME(8, 0, 0)
                                            )
                                        )
                                            +
                                        MAX(
                                            0,
                                            MIN(
                                                Corte,
                                                D + TIME(17, 0, 0)
                                            )
                                                -
                                            MAX(
                                                Inicio,
                                                D + TIME(13, 0, 0)
                                            )
                                        )
                                    )
                            )

                        RETURN
                            DIVIDE(
                                Decorrido,
                                Duracao,
                                0
                            )
                    )

                RETURN
                    Peso * Fracao,

                0
            )
    )

RETURN
IF(
    ISBLANK(DataEixo)
        ||
    COUNTROWS(Atividades) = 0
        ||
    Invalidas > 0
        ||
    Total <= 0,
    BLANK(),
    DIVIDE(
        Previsto,
        Total,
        0
    )
)
```

## dFase Engenharia 30% — ADRESSING 30

Uso: LDP - 3D - 30% / `6308f22bd4b363b98781`; LDP - Engineering - 30% / `2d7506eb935d84cc43d9`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[DOCUMENT STATUS 30%] = "ADRESSING COMMENTS"
)
```

## dFase Engenharia 30% — CANCELED 30

Uso: LDP - 3D - 30% / `c48415c75384d07c76b6`; LDP - Engineering - 30% / `9aa325983a72e51c5a5f`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[SITUATION 30%] = "Canceled"
)
```

## dFase Engenharia 30% — CLIENT REVIEW 30

Uso: LDP - 3D - 30% / `de52f6324b02606efc96`; LDP - Engineering - 30% / `1c6e40b19e78ec0753de`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[DOCUMENT STATUS 30%] = "CLIENT REVIEW"
)
```

## dFase Engenharia 30% — ISSUED 30

Uso: LDP - 3D - 30% / `e84b23242144306cef9d`; LDP - Engineering - 30% / `3ed78afe5dd6a6332962`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[SITUATION 30%] = "Issued"
)
```

## dFase Engenharia 60% — ADRESSING 60

Uso: LDP - 3D - 60% / `a889a460fc54d880ee75`; LDP - Engineering - 60% / `e807f67b89dbbec173f2`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[DOCUMENT STATUS 60%] = "ADRESSING COMMENTS"
)
```

## dFase Engenharia 60% — CANCELED 60

Uso: LDP - 3D - 60% / `fcb7f8a429e998b114d2`; LDP - Engineering - 60% / `9b1eefccb3738e63ad1d`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[SITUATION 60%] = "Canceled"
)
```

## dFase Engenharia 60% — CLIENT REVIEW 60

Uso: LDP - 3D - 60% / `e2df5cc8e3046c8947f5`; LDP - Engineering - 60% / `67a267ff45b19f11dd73`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[DOCUMENT STATUS 60%] = "CLIENT REVIEW"
)
```

## dFase Engenharia 60% — ISSUED 60

Uso: LDP - 3D - 60% / `c8ef976d5ec64de09ba5`; LDP - Engineering - 60% / `c370079fb7c02005858a`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[SITUATION 60%] = "Issued"
)
```

## dFase Engenharia 90% — ADRESSING 90

Uso: LDP - 3D - 90% / `c2ba931a184a07f0248a`; LDP - Engineering - 90% / `f402115663ab01639058`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[DOCUMENT STATUS 90%] = "ADRESSING COMMENTS"
)
```

## dFase Engenharia 90% — CANCELED 90

Uso: LDP - 3D - 90% / `947d01e906caadf8bdd3`; LDP - Engineering - 90% / `14ff715b6cc8447366f7`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[SITUATION 90%] = "Canceled"
)
```

## dFase Engenharia 90% — CLIENT REVIEW 90

Uso: LDP - 3D - 90% / `f476e3ebfbfbac695fb4`; LDP - Engineering - 90% / `6aa4cb19df045d0f1310`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[DOCUMENT STATUS 90%] = "CLIENT REVIEW"
)
```

## dFase Engenharia 90% — ISSUED 90

Uso: LDP - 3D - 90% / `291e9db4fa498824a995`; LDP - Engineering - 90% / `b8a28b51bdb0eff0036f`.

```DAX
CALCULATE(
    COUNTROWS('LDP (4)'),
    'LDP (4)'[SITUATION 90%] = "Issued"
)
```

## Observações de implementação

- `ALLSELECTED(WBX)` preserva segmentações do usuário; substituí-lo por todas as linhas muda o denominador.
- Medidas `CURVA` são acumuladas; `COLUNA` é o incremento da semana.
- O realizado é limitado pela data de corte e retorna BLANK para semanas futuras.
- As medidas 3D usam `TREATAS` dos números de documento de `LDP (4)` para `WBX[NA DOCUMENT NUMBER]`.
- Há medidas legadas referenciadas somente como filtros sem condição; não confundir presença no JSON com série ativa.
