/*
  Carlos Pineda Guerrero, septiembre 2024
*/

package servicio;

import java.sql.Timestamp;

public class Usuario
{
  public String email;
  public String password;
  public String nombre;
  public String apellido_paterno;
  public String apellido_materno;
  public Timestamp fecha_nacimiento;
  public Long telefono;
  public String genero;
  public byte[] foto;
}
