import { createContext, useContext, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { AuthUser } from "@sbm/shared";
import { api } from "./api";

interface AuthValue {
  user: AuthUser | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<AuthUser>;
  logout: () => Promise<void>;
}
const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const me = useQuery({ queryKey: ["auth", "me"], queryFn: api.me, retry: false });
  const loginMutation = useMutation({ mutationFn: ({ username, password }: { username: string; password: string }) => api.login(username, password) });
  const login = async (username: string, password: string) => { const user = await loginMutation.mutateAsync({ username, password }); qc.setQueryData(["auth", "me"], user); return user; };
  const logout = async () => { await api.logout(); qc.clear(); window.location.assign("/login"); };
  return <AuthContext.Provider value={{ user: me.data ?? null, loading: me.isLoading, login, logout }}>{children}</AuthContext.Provider>;
}
export function useAuth() { const value = useContext(AuthContext); if (!value) throw new Error("AuthProvider missing"); return value; }
