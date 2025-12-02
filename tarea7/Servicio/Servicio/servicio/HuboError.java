/*
  Error.java
  Permite regresar al cliente REST un mensaje de error
  Carlos Pineda Guerrero, septiembre 2024
*/

package servicio;

public class HuboError
{
	public String message;

	HuboError(String message)
	{
		this.message = message;
	}
}
