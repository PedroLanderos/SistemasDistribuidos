#Definir y descomentar la siguiente variable de entorno:
export CATALINA_HOME=<ruta-absoluta-del-directorio-de-Tomcat>
javac -cp "$CATALINA_HOME/lib/*:." servicio/Servicio.java
rm WEB-INF/classes/servicio/*
cp servicio/*.class WEB-INF/classes/servicio/.
jar cvf Servicio.war WEB-INF META-INF
rm -rf $CATALINA_HOME/webapps/Servicio.war $CATALINA_HOME/webapps/Servicio
cp Servicio.war $CATALINA_HOME/webapps/.
