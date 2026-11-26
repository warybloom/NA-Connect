IF SCHEMA_ID(N'planning') IS NULL EXEC(N'CREATE SCHEMA planning');
GO
IF OBJECT_ID(N'planning.ReviewFlow', N'U') IS NULL
CREATE TABLE planning.ReviewFlow (
    Id uniqueidentifier NOT NULL CONSTRAINT PK_ReviewFlow PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    Active bit NOT NULL,
    Version int NOT NULL,
    StagesJson nvarchar(max) NOT NULL CONSTRAINT CK_ReviewFlow_StagesJson CHECK (ISJSON(StagesJson) = 1)
);
GO
IF OBJECT_ID(N'planning.ReviewFlowBinding', N'U') IS NULL
CREATE TABLE planning.ReviewFlowBinding (
    Id uniqueidentifier NOT NULL CONSTRAINT PK_ReviewFlowBinding PRIMARY KEY,
    FlowId uniqueidentifier NOT NULL CONSTRAINT FK_ReviewFlowBinding_Flow REFERENCES planning.ReviewFlow(Id),
    Scope varchar(10) NOT NULL CONSTRAINT CK_ReviewFlowBinding_Scope CHECK (Scope IN ('project','group')),
    ProjectId nvarchar(100) NULL,
    GroupName nvarchar(200) NULL,
    ProjectIdsJson nvarchar(max) NOT NULL CONSTRAINT CK_ReviewFlowBinding_ProjectsJson CHECK (ISJSON(ProjectIdsJson) = 1),
    Version int NOT NULL,
    CONSTRAINT CK_ReviewFlowBinding_Target CHECK ((Scope='project' AND ProjectId IS NOT NULL AND GroupName IS NULL) OR (Scope='group' AND ProjectId IS NULL AND GroupName IS NOT NULL))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'planning.ReviewFlowBinding') AND name=N'UX_ReviewFlowBinding_Project')
CREATE UNIQUE INDEX UX_ReviewFlowBinding_Project ON planning.ReviewFlowBinding(ProjectId) WHERE Scope='project';
GO
