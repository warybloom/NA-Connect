// Arquivo: ClientApp/src/components/app-sidebar.tsx

import * as React from "react"
import { Link, useParams } from "react-router-dom";
import {
  AudioWaveform,
  BarChart3,
  Book,
  BookOpen,
  Bot,
  BotIcon,
  Box,
  ChartBarStacked,
  ChartNoAxesColumn,
  ChartScatter,
  ChartSplineIcon,
  ClipboardList,
  Clock,
  Cog,
  Columns,
  Command,
  ContainerIcon,
  Earth,
  Eye,
  FileText,
  Frame,
  GalleryHorizontal,
  GalleryThumbnailsIcon,
  GalleryVerticalEnd,
  Grape,
  Home,
  Icon,
  LayoutGrid,
  Map,
  PencilLine,
  PieChart,
  Scroll,
  Settings,
  Settings2,
  Shapes,
  ToolCase,
  TrendingUp,
} from "lucide-react"

import { NavMain } from "@/components/nav-main"
import { NavProjects } from "@/components/nav-projects"
import { NavUser } from "@/components/nav-user"
import { TeamSwitcher } from "@/components/team-switcher"

import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from "@/components/ui/sidebar"
import { Logo } from "./ui/logo"

const data = {
  user: {
    name: "shadcn",
    email: "m@example.com",
    avatar: "/avatars/shadcn.jpg",  
  },
  teams: [
    {
      name: "N&A Consultores",
      logo: Logo,
    },
    {
      name: "Painel de Controle",
      url: "/control-panel",
      Icon: Earth,
      plan: "Startup",
    },
    {
      name: "Evil Corp.",
      logo: Command,
      plan: "Free",
    },
  ],
  // 🔴 SUBMENUS REMOVIDOS AQUI - Apenas título, URL e Ícone:
  // Caminho: ClientApp/src/components/app-sidebar.tsx

  navMain: [

    {
      title: "Painel de Controle",
      url: "/projects",
      icon: LayoutGrid,
    },
    {
      title: "Relatorio (GEN)",
      url: "/relatorio-gen",
      icon: BarChart3,
    },
    {
      title: "Lançamentos PMO",
      url: "/lançamentos-pmo",
      icon: PencilLine,
    },
    {
      title: "Motor de Templates",
      url: "/motor-templates",
      icon: FileText,
    },
    {
      title: "Angedamento Robô",
      url: "/agend-robo",
      icon: Clock,
    },
    {
      title: "Configurações",
      url: "/painel-de-controle",
      icon: Settings,
    },
    
    {
      title: "Calibração de Visão",
      url: "/calibração-visão",
      icon: Eye,
    },
    {
      title: "Configurar LDP",
      url: "/confi-ldp",
      icon: ClipboardList,
    },
    {
      title: "Auditoria Plant 3D",
      url: "/auditoria-platn3d",
      icon: Shapes,
    },
        {
      title: "Andamento Robô",
      url: "/andamento-robo",
      icon: BotIcon,
    },
  ],
}

import { useState } from "react"
import { OCRConfigDialog } from "@/pages/ocr-config-dialog"
import { useCurrentUser } from "@/components/hooks/use-current-user"
import { ChartStyle } from "./ui/chart"

export function AppSidebar({ ...props }: React.ComponentProps<typeof Sidebar>) {
  const { user, loading } = useCurrentUser()
  const [ocrDialogOpen, setOcrDialogOpen] = useState(false)
  const { hubId } = useParams() // 👈 pega o hubId da rota atual, se existir

  // Monta a lista de itens, substituindo a url do Home dinamicamente
  const navItems = data.navMain.map((item) =>
    item.title === "Home"
      ? { ...item, url: hubId ? `/projects/${hubId}` : "/dashboard" }
      : item
  )

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader className="flex items-center gap-1 p-1">
        <Logo />
      </SidebarHeader>

      <SidebarContent>
        <NavMain
          items={navItems}
          onAction={(action) => {
            if (action === "open-ocr-config") setOcrDialogOpen(true)
          }}
        />
      </SidebarContent>
      <SidebarFooter>
        {!loading && user && <NavUser user={user} />}
      </SidebarFooter>
      <SidebarRail />

      <OCRConfigDialog isOpen={ocrDialogOpen} onClose={() => setOcrDialogOpen(false)} />
    </Sidebar>
  )
}