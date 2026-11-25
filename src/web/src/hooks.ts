import { useQuery } from '@tanstack/react-query'
import { api, ApiError, type Entry, type FuelLog, type FuelUp, type Me, type Photo, type TagCount, type Vehicle } from './api'

export const keys = {
  me: ['me'] as const,
  vehicles: ['vehicles'] as const,
  vehicle: (id: number) => ['vehicles', id] as const,
  entries: (vehicleId: number) => ['entries', 'vehicle', vehicleId] as const,
  entry: (id: number) => ['entries', id] as const,
  photos: (vehicleId: number) => ['photos', vehicleId] as const,
  tags: ['tags'] as const,
  fuel: (vehicleId: number) => ['fuel', 'vehicle', vehicleId] as const,
  fuelUp: (id: number) => ['fuel', id] as const,
}

export function useMe() {
  return useQuery({
    queryKey: keys.me,
    queryFn: async () => {
      try {
        return await api.get<Me>('/auth/me')
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null
        throw e
      }
    },
    staleTime: 5 * 60_000,
  })
}

export const useVehicles = () => useQuery({ queryKey: keys.vehicles, queryFn: () => api.get<Vehicle[]>('/vehicles') })

export const useVehicle = (id: number) =>
  useQuery({ queryKey: keys.vehicle(id), queryFn: () => api.get<Vehicle>(`/vehicles/${id}`), enabled: id > 0 })

export const useEntries = (vehicleId: number) =>
  useQuery({ queryKey: keys.entries(vehicleId), queryFn: () => api.get<Entry[]>(`/vehicles/${vehicleId}/entries`), enabled: vehicleId > 0 })

export const useEntry = (id: number) =>
  useQuery({ queryKey: keys.entry(id), queryFn: () => api.get<Entry>(`/entries/${id}`), enabled: id > 0 })

export const usePhotos = (vehicleId: number) =>
  useQuery({ queryKey: keys.photos(vehicleId), queryFn: () => api.get<Photo[]>(`/vehicles/${vehicleId}/photos`), enabled: vehicleId > 0 })

export const useTags = () => useQuery({ queryKey: keys.tags, queryFn: () => api.get<TagCount[]>('/tags') })

export const useFuel = (vehicleId: number) =>
  useQuery({ queryKey: keys.fuel(vehicleId), queryFn: () => api.get<FuelLog>(`/vehicles/${vehicleId}/fuel`), enabled: vehicleId > 0 })

export const useFuelUp = (id: number) =>
  useQuery({ queryKey: keys.fuelUp(id), queryFn: () => api.get<FuelUp>(`/fuel/${id}`), enabled: id > 0 })
