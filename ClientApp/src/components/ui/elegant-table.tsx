// Caminho: ClientApp/src/components/ui/elegant-table.tsx (VERSÃO COM MELHORIA DE ORDENAÇÃO)

import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Button } from "@/components/ui/button"
import { Download, Eye, Folder, File as FileIcon } from "lucide-react"
import { cn } from "@/lib/utils"
import {
  flexRender,
  getCoreRowModel,
  useReactTable,
  ColumnDef,
} from "@tanstack/react-table"
import { useMemo } from "react"

export interface TableRowData {
  id: string;
  type: 'folder' | 'item';
  name: string;
  lastModified: string;
  storageUrn?: string;
  customAttributes: Record<string, any>; 
}

interface ElegantTableProps {
  data: TableRowData[];
  onFolderClick: (folderId: string) => void;
  onDownloadClick: (storageUrn: string) => void;
  onVersionsClick: (itemId: string, itemName: string) => void; 
  className?: string;
}

export function ElegantTable({ data, onFolderClick, onDownloadClick, onVersionsClick, className }: ElegantTableProps) {
  
  const attributeHeaders: string[] = useMemo(() => {
    const headers = new Set<string>();
    data.forEach(item => {
      if (item.customAttributes) {
        Object.keys(item.customAttributes).forEach(key => headers.add(key));
      }
    });
    // MUDANÇA: Adicionado .sort() para garantir uma ordem alfabética consistente das colunas.
    return Array.from(headers).sort();
  }, [data]);

  const columns = useMemo<ColumnDef<TableRowData>[]>(() => [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ row }) => {
        const item = row.original;
        return (
          <div className="flex items-center gap-3">
            {item.type === 'folder' ? <Folder className="w-5 h-5 text-primary" /> : <FileIcon className="w-5 h-5 text-muted-foreground" />}
            {item.type === 'folder' ? (
               <a 
                 href="#" 
                 onClick={(e) => { e.preventDefault(); onFolderClick(item.id); }} 
                 className="hover:underline cursor-pointer font-medium text-foreground"
               >
                 {item.name}
               </a>
            ) : (
              <span className="font-medium text-foreground">{item.name}</span>
            )}
          </div>
        );
      },
      size: 350,
      minSize: 300, 
    },
    {
      accessorKey: 'lastModified',
      header: 'Última Modificação',
      size: 200,
      minSize: 180,
    },
    ...attributeHeaders.map((header): ColumnDef<TableRowData> => ({
      accessorKey: `customAttributes.${header}`,
      header: header,
      size: 150,
      minSize: 120,
      cell: ({ getValue }) => <span className="text-muted-foreground">{String(getValue() ?? '---')}</span>,
    })),
    {
      id: 'actions',
      header: () => <div className="text-right">Ações</div>,
      cell: ({ row }) => {
        const item = row.original;
        if (item.type !== 'item') return null;
        return (
          <div className="flex gap-2 justify-end opacity-0 group-hover:opacity-100 transition-all duration-300">
            <Button size="sm" variant="outline" onClick={() => onDownloadClick(item.storageUrn || '')} disabled={!item.storageUrn}>
              <Download className="w-4 h-4 mr-1" /> Download
            </Button>
            <Button size="sm" variant="outline" onClick={() => onVersionsClick(item.id, item.name)}>
              <Eye className="w-4 h-4 mr-1" /> Ver Versões
            </Button>
          </div>
        );
      },
      size: 220,
      enableResizing: false,
    }
  ], [attributeHeaders, onFolderClick, onDownloadClick, onVersionsClick]);

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
    columnResizeMode: 'onChange',
  });

  return (
    <div className={cn("elegant-table", className)}>
      <Table className="w-full" style={{ tableLayout: 'fixed' }}>
        <TableHeader>
          {table.getHeaderGroups().map(headerGroup => (
            <TableRow key={headerGroup.id}>
              {headerGroup.headers.map(header => (
                <TableHead key={header.id} colSpan={header.colSpan} style={{ width: header.getSize() }} className="group">
                  {header.isPlaceholder ? null : flexRender(header.column.columnDef.header, header.getContext())}
                  <div
                    onMouseDown={header.getResizeHandler()}
                    onTouchStart={header.getResizeHandler()}
                    className={cn("resizer", header.column.getIsResizing() && "isResizing", !header.column.getCanResize() && "hidden")}
                  ></div>
                </TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {table.getRowModel().rows.map(row => (
            <TableRow key={row.id} className="group">
              {row.getVisibleCells().map(cell => (
                <TableCell key={cell.id} style={{ width: cell.column.getSize() }} className="truncate">
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}