import { useEffect, useState } from "react";

interface CurrentUser{
    name: string
    email: string
    avatar: string
}

export function useCurrentUser(){
    const[user, setUser] = useState<CurrentUser | null>(null)
    const[loading, setLoading] = useState(true)
    




useEffect(() => {
    fetch("/Authentication/OAuth/GetLoggedInUserName", {
        credentials: "include"
    })
        .then((res) => {
            if (!res.ok) throw new Error("Não autenticado");
            return res.json();
        })
        .then((data: { name: string }) => setUser({ name: data.name, email: "", avatar: "" }))
        .catch(() => setUser(null))
        .finally(() => setLoading(false));
}, []);
    return {user, loading}
}


